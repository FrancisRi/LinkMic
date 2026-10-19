package dev.linkmic.app;

import android.content.Context;
import android.security.keystore.*;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.security.*;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;

/** Credentials stay in app-private storage, encrypted with an Android Keystore key. */
public final class TrustedComputers implements DevicePairing.Trust {
    public static final class Computer {
        public final String id,name;
        public Computer(String id,String name){this.id=id;this.name=name;}
    }
    interface Records {
        boolean exists();
        JSONObject load()throws IOException;
        void save(JSONObject state)throws IOException;
        void delete();
    }
    private static final Object LOCK=new Object();
    private final Records records;
    public TrustedComputers(Context context){this(new EncryptedFile(context));}
    TrustedComputers(Records records){this.records=records;}
    private JSONObject load()throws IOException{
        JSONObject state=records.load();
        try{if(!state.getString("phoneId").matches("[0-9a-f]{32}") || state.getJSONObject("computers").length()>32)throw new AppError(AppError.Code.TRUST_UNAVAILABLE);}
        catch(JSONException error){throw new AppError(AppError.Code.TRUST_UNAVAILABLE,error);}
        return state;
    }
    private static JSONObject entry(JSONObject computers,String id)throws JSONException,IOException{
        Object value=computers.get(id);
        // Existing releases stored only a Base64 string. Reading never discards it.
        if(value instanceof String)return new JSONObject().put("token",value).put("name","");
        if(value instanceof JSONObject)return (JSONObject)value;
        throw new AppError(AppError.Code.TRUST_UNAVAILABLE);
    }
    private static String cleanName(String name){
        if(name==null)return "";
        String clean=name.replaceAll("[\\p{Cntrl}\\u202A-\\u202E\\u2066-\\u2069]","").trim();
        return clean.length()>80?clean.substring(0,80):clean;
    }
    @Override public String phoneId(){synchronized(LOCK){try{JSONObject state=load();if(!records.exists())records.save(state);return state.getString("phoneId");}catch(IOException|JSONException error){throw new IllegalStateException(new AppError(AppError.Code.TRUST_UNAVAILABLE,error));}}}
    @Override public byte[] tokenFor(String computer)throws IOException{synchronized(LOCK){
        try{
            JSONObject computers=load().getJSONObject("computers");if(!computers.has(computer))return null;
            byte[] bytes=Base64.getDecoder().decode(entry(computers,computer).getString("token"));
            if(bytes.length!=32)throw new AppError(AppError.Code.TRUST_UNAVAILABLE);return bytes;
        }catch(JSONException|IllegalArgumentException error){throw new AppError(AppError.Code.TRUST_UNAVAILABLE,error);}
    }}
    @Override public void remember(String computer,byte[] token)throws IOException{remember(computer,token,"");}
    @Override public void remember(String computer,byte[] token,String name)throws IOException{
        if(!computer.matches("[0-9a-f]{32}") || token.length!=32)throw new AppError(AppError.Code.TRUST_UNAVAILABLE);
        synchronized(LOCK){try{
            JSONObject state=load(),computers=state.getJSONObject("computers");
            if(computers.length()>=32 && !computers.has(computer))throw new AppError(AppError.Code.TOO_MANY_COMPUTERS);
            String display=cleanName(name);
            if(display.isEmpty() && computers.has(computer))display=cleanName(entry(computers,computer).optString("name",""));
            computers.put(computer,new JSONObject().put("token",Base64.getEncoder().encodeToString(token)).put("name",display));records.save(state);
        }catch(JSONException error){throw new AppError(AppError.Code.TRUST_SAVE_FAILED,error);}}
    }
    public List<Computer> list()throws IOException{synchronized(LOCK){
        if(!records.exists())return Collections.emptyList();
        try{
            JSONObject computers=load().getJSONObject("computers");List<Computer> result=new ArrayList<>();
            Iterator<String> ids=computers.keys();while(ids.hasNext()){
                String id=ids.next();if(!id.matches("[0-9a-f]{32}"))throw new AppError(AppError.Code.TRUST_UNAVAILABLE);
                result.add(new Computer(id,cleanName(entry(computers,id).optString("name",""))));
            }
            result.sort(Comparator.comparing((Computer c)->c.name,String.CASE_INSENSITIVE_ORDER).thenComparing(c->c.id));return Collections.unmodifiableList(result);
        }catch(JSONException error){throw new AppError(AppError.Code.TRUST_UNAVAILABLE,error);}
    }}
    public int count()throws IOException{return list().size();}
    /** Returns remaining count only after an atomic save succeeds. Never resets other records on failure. */
    public int forget(Set<String> selected)throws IOException{synchronized(LOCK){
        for(String id:selected)if(id==null || !id.matches("[0-9a-f]{32}"))throw new AppError(AppError.Code.BAD_PAIRING_DATA);
        if(!records.exists())return 0;
        try{
            JSONObject state=load(),computers=state.getJSONObject("computers");boolean changed=false;
            for(String id:selected)if(computers.has(id)){computers.remove(id);changed=true;}
            if(changed)records.save(state);return computers.length();
        }catch(JSONException error){throw new AppError(AppError.Code.TRUST_SAVE_FAILED,error);}
    }}
    public void forgetAll(){synchronized(LOCK){
        if(!records.exists())return;
        try{JSONObject state=load();state.put("computers",new JSONObject());records.save(state);}
        catch(IOException|JSONException error){records.delete();}
    }}
    private static final class EncryptedFile implements Records {
        private static final String ALIAS="LinkMicPairing-v1";
        private final AtomicFile file;
        EncryptedFile(Context context){file=new AtomicFile(new File(context.getNoBackupFilesDir(),"paired-computers.enc"));}
        public boolean exists(){return file.getBaseFile().exists()||new File(file.getBaseFile().getPath()+".bak").exists();}
        private SecretKey key()throws GeneralSecurityException,IOException{
            KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
            if(store.containsAlias(ALIAS))return (SecretKey)store.getKey(ALIAS,null);
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());
            return generator.generateKey();
        }
        public JSONObject load()throws IOException{
            if(!exists()){try{return new JSONObject().put("phoneId",DevicePairing.newId()).put("computers",new JSONObject());}catch(JSONException impossible){throw new IOException(impossible);}}
            if(file.getBaseFile().length()>65536 || new File(file.getBaseFile().getPath()+".bak").length()>65536)throw new AppError(AppError.Code.TRUST_UNAVAILABLE);
            byte[] clear=null;
            try{
                byte[] data=file.readFully();if(data.length<28)throw new AppError(AppError.Code.TRUST_UNAVAILABLE);
                Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,data,0,12));clear=cipher.doFinal(data,12,data.length-12);
                return new JSONObject(new String(clear,java.nio.charset.StandardCharsets.UTF_8));
            }catch(GeneralSecurityException|JSONException error){throw new AppError(AppError.Code.TRUST_UNAVAILABLE,error);}
            finally{if(clear!=null)Arrays.fill(clear,(byte)0);}
        }
        public void save(JSONObject state)throws IOException{
            byte[] clear=state.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);FileOutputStream output=null;
            try{
                Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());byte[] encrypted=cipher.doFinal(clear);
                output=file.startWrite();output.write(cipher.getIV());output.write(encrypted);file.finishWrite(output);output=null;
            }catch(GeneralSecurityException error){throw new AppError(AppError.Code.TRUST_SAVE_FAILED,error);}
            finally{if(output!=null)file.failWrite(output);Arrays.fill(clear,(byte)0);}
        }
        public void delete(){file.delete();}
    }
}
