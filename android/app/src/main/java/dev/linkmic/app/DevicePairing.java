package dev.linkmic.app;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.interfaces.ECPublicKey;
import java.security.spec.*;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.*;

/** First-use PIN enrollment; subsequent mutual challenge authentication. */
public final class DevicePairing {
    public interface Trust {
        String phoneId();
        byte[] tokenFor(String computerId) throws IOException;
        void remember(String computerId, byte[] token) throws IOException;
        default void remember(String computerId,byte[] token,String computerName)throws IOException{remember(computerId,token);}
    }
    private static final SecureRandom RANDOM=new SecureRandom();
    private static byte[] utf8(String value){return value.getBytes(StandardCharsets.UTF_8);}
    private static byte[] random(int count){byte[] value=new byte[count];RANDOM.nextBytes(value);return value;}
    public static String newId(){return hex(random(16));}
    private static String hex(byte[] data){StringBuilder value=new StringBuilder();for(byte b:data)value.append(String.format(Locale.ROOT,"%02x",b&255));return value.toString();}
    private static byte[] decode(String value,int maximum)throws IOException{
        if(value.length()>maximum*2)throw new AppError(AppError.Code.BAD_PAIRING_DATA);
        try{byte[] data=Base64.getDecoder().decode(value);if(data.length>maximum)throw new AppError(AppError.Code.BAD_PAIRING_DATA);return data;}
        catch(IllegalArgumentException error){throw new AppError(AppError.Code.BAD_PAIRING_DATA,error);}
    }
    private static byte[] hmac(byte[] key,byte[] data)throws GeneralSecurityException{Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(key,"HmacSHA256"));return mac.doFinal(data);}
    private static String proof(byte[] token,String context)throws GeneralSecurityException{return hex(hmac(token,utf8(context)));}
    private static byte[] handshakeKey(byte[] shared,String transcript)throws GeneralSecurityException{
        byte[] prk=hmac(MessageDigest.getInstance("SHA-256").digest(utf8(transcript)),shared);
        try{return hmac(prk,utf8("LinkMic handshake v2\u0001"));}finally{Arrays.fill(prk,(byte)0);}
    }
    private static String seal(byte[] key,String clear,String context)throws GeneralSecurityException{
        byte[] nonce=random(12);Cipher aes=Cipher.getInstance("AES/GCM/NoPadding");aes.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,nonce));aes.updateAAD(utf8(context));byte[] cipher=aes.doFinal(utf8(clear));
        byte[] envelope=new byte[12+cipher.length];System.arraycopy(nonce,0,envelope,0,12);System.arraycopy(cipher,0,envelope,12,cipher.length);return Base64.getEncoder().encodeToString(envelope);
    }
    private static String open(byte[] key,String encoded,String context)throws GeneralSecurityException,IOException{
        byte[] envelope=decode(encoded,512);if(envelope.length<28)throw new AppError(AppError.Code.BAD_PAIRING_DATA);
        Cipher aes=Cipher.getInstance("AES/GCM/NoPadding");aes.init(Cipher.DECRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,envelope,0,12));aes.updateAAD(utf8(context));return new String(aes.doFinal(envelope,12,envelope.length-12),StandardCharsets.UTF_8);
    }
    private static String readLine(InputStream input)throws IOException{
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        while(bytes.size()<1024){int b=input.read();if(b<0)throw new AppError(AppError.Code.DISCONNECTED);if(b==10)return bytes.toString(StandardCharsets.UTF_8.name()).trim();bytes.write(b);}throw new AppError(AppError.Code.BAD_PAIRING_DATA);
    }
    private static void send(OutputStream output,String value)throws IOException{output.write(utf8(value+"\n"));output.flush();}
    public static long pair(InputStream input,OutputStream output,String pin,String name,Trust trust)throws IOException{
        byte[] key=null,saved=null;
        try{
            KeyPairGenerator generator=KeyPairGenerator.getInstance("EC");generator.initialize(new ECGenParameterSpec("secp256r1"));KeyPair ephemeral=generator.generateKeyPair();
            String safe=name.replaceAll("[^A-Za-z0-9._-]","_");if(safe.isEmpty())safe="Android";if(safe.length()>30)safe=safe.substring(0,30);
            String hello="LMIC/3 "+trust.phoneId()+" "+Base64.getEncoder().encodeToString(ephemeral.getPublic().getEncoded())+" "+Base64.getEncoder().encodeToString(random(32))+" "+safe;
            send(output,hello);String[] challenge=readLine(input).split(" ");
            if(challenge.length!=6 || !challenge[0].equals("CHALLENGE"))throw new AppError(AppError.Code.DESKTOP_UPDATE_REQUIRED);
            String computer=challenge[1];if(!computer.matches("[0-9a-f]{32}") || decode(challenge[3],32).length!=32 || !(challenge[4].equals("PAIR")||challenge[4].equals("AUTH")))throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            String transcript=hello+"|"+computer+"|"+challenge[2]+"|"+challenge[3]+"|"+challenge[4];
            saved=trust.tokenFor(computer);
            if(saved!=null && challenge[4].equals("AUTH") && !MessageDigest.isEqual(utf8(challenge[5]),utf8(proof(saved,"SERVER|"+transcript))))throw new AppError(AppError.Code.COMPUTER_AUTH_FAILED);
            boolean resume=saved!=null && challenge[4].equals("AUTH");
            if(!resume && (pin==null || !pin.matches("[0-9]{6}"))){send(output,"PAIR_REQUIRED");throw new AppError(AppError.Code.PAIR_REQUIRED);}
            PublicKey serverKey=KeyFactory.getInstance("EC").generatePublic(new X509EncodedKeySpec(decode(challenge[2],128)));
            if(!(serverKey instanceof ECPublicKey))throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            ECParameterSpec actual=((ECPublicKey)serverKey).getParams();AlgorithmParameters parameters=AlgorithmParameters.getInstance("EC");parameters.init(new ECGenParameterSpec("secp256r1"));ECParameterSpec expected=parameters.getParameterSpec(ECParameterSpec.class);
            if(!actual.getCurve().equals(expected.getCurve()) || !actual.getGenerator().equals(expected.getGenerator()) || !actual.getOrder().equals(expected.getOrder()) || actual.getCofactor()!=expected.getCofactor())throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            KeyAgreement agreement=KeyAgreement.getInstance("ECDH");agreement.init(ephemeral.getPrivate());agreement.doPhase(serverKey,true);byte[] shared=agreement.generateSecret();key=handshakeKey(shared,transcript);Arrays.fill(shared,(byte)0);
            String login=resume?"AUTH "+proof(saved,"CLIENT|"+transcript):"PAIR "+pin;
            send(output,"PROOF "+seal(key,login,transcript+"|CLIENT"));String reply=readLine(input);
            if(!reply.startsWith("OK2 "))throw new AppError(AppError.Code.PAIRING_MISMATCH);
            String[] accepted=open(key,reply.substring(4),transcript+"|SERVER").split(" ");
            if(accepted.length!=4 || !accepted[0].equals("OK"))throw new AppError(AppError.Code.DESKTOP_UPDATE_REQUIRED);
            String computerName=new String(decode(accepted[3],320),StandardCharsets.UTF_8);
            if(computerName.trim().isEmpty() || computerName.length()>80)throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            long session=Long.parseLong(accepted[1]);if(session<=0 || session>0xffffffffL)throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            byte[] credential=decode(accepted[2],32);if(credential.length!=32)throw new AppError(AppError.Code.BAD_PAIRING_DATA);
            try{
                if(resume && !MessageDigest.isEqual(saved,credential))throw new AppError(AppError.Code.CREDENTIAL_CHANGED);
                // Also enrich old token-only records and refresh renamed computers on reconnect.
                trust.remember(computer,credential,computerName);
            }finally{Arrays.fill(credential,(byte)0);}
            return session;
        }catch(GeneralSecurityException|IllegalArgumentException error){throw new AppError(AppError.Code.BAD_PAIRING_DATA,error);}
        finally{if(key!=null)Arrays.fill(key,(byte)0);if(saved!=null)Arrays.fill(saved,(byte)0);}
    }
}
