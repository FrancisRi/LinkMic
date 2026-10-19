import dev.linkmic.app.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

public final class PairingClientFixture {
    private static final class Trust implements DevicePairing.Trust {
        String id=DevicePairing.newId();final Map<String,byte[]> tokens=new HashMap<>();String computerName;
        public String phoneId(){return id;}
        public byte[] tokenFor(String computer){byte[] value=tokens.get(computer);return value==null?null:value.clone();}
        public void remember(String computer,byte[] token){tokens.put(computer,token.clone());}
        public void remember(String computer,byte[] token,String name){remember(computer,token);computerName=name;}
        Trust copy(){Trust other=new Trust();other.id=id;tokens.forEach((k,v)->other.tokens.put(k,v.clone()));return other;}
    }
    private static int port;private static byte[] replay;
    private static void pass(String label){System.out.println("PASS "+label);}
    private static void connect(Trust trust,String code,boolean reject,boolean tamperChallenge,boolean record,boolean tamperProof)throws Exception{
        try(Socket socket=new Socket("127.0.0.1",port)){
            socket.setSoTimeout(10000);InputStream input=socket.getInputStream();OutputStream original=socket.getOutputStream();ByteArrayOutputStream observed=new ByteArrayOutputStream();
            if(tamperChallenge){
                InputStream source=input;input=new InputStream(){InputStream line;
                    private void prepare()throws IOException{if(line!=null)return;ByteArrayOutputStream bytes=new ByteArrayOutputStream();for(int i=0;i<1024;i++){int b=source.read();if(b<0)throw new EOFException();if(b==10)break;bytes.write(b);}String value=bytes.toString("UTF-8");int last=value.lastIndexOf(' ');value=value.substring(0,last+1)+"0".repeat(64)+"\n";line=new ByteArrayInputStream(value.getBytes(StandardCharsets.UTF_8));}
                    public int read()throws IOException{prepare();int b=line.read();return b>=0?b:source.read();}
                };
            }
            OutputStream output=new OutputStream(){public void write(int b)throws IOException{original.write(b);}public void write(byte[] bytes,int start,int count)throws IOException{
                byte[] data=Arrays.copyOfRange(bytes,start,start+count);observed.write(data);
                String text=new String(data,StandardCharsets.UTF_8);
                if(tamperProof&&text.startsWith("PROOF ")){byte[] sealed=Base64.getDecoder().decode(text.substring(6).trim());sealed[13]^=1;data=("PROOF "+Base64.getEncoder().encodeToString(sealed)+"\n").getBytes(StandardCharsets.UTF_8);}
                original.write(data);
            }public void flush()throws IOException{original.flush();}};
            try{
                long session=DevicePairing.pair(input,output,code,"JavaFixture",trust);
                if(reject)throw new AssertionError("Invalid pairing accepted");
                output.write(Wire.encode(new short[960],77,0,session));output.flush();
                if(record)replay=observed.toByteArray();
            }catch(IOException expected){if(!reject)throw expected;}
        }
    }
    public static void main(String[] args)throws Exception{
        port=Integer.parseInt(args[0]);Trust trust=new Trust();
        connect(trust,"123456",false,false,false,false);if(trust.tokens.size()!=1 || !"书房电脑".equals(trust.computerName))throw new AssertionError("First pairing/name not remembered");pass("first enrollment with authenticated Unicode computer name");
        connect(trust,"",false,false,true,false);pass("reconnect without PIN");
        // Simulate a restarted phone process with persisted app identity and credentials.
        Path state=Paths.get(args[1],"java-fixture-trust.properties");Properties properties=new Properties();properties.setProperty("phoneId",trust.id);trust.tokens.forEach((k,v)->properties.setProperty(k,Base64.getEncoder().encodeToString(v)));
        try(OutputStream file=Files.newOutputStream(state)){properties.store(file,"Synthetic test credentials only; never included in product archives");}
        Properties loaded=new Properties();try(InputStream file=Files.newInputStream(state)){loaded.load(file);}Trust restarted=new Trust();restarted.id=loaded.getProperty("phoneId");for(String key:loaded.stringPropertyNames())if(!key.equals("phoneId"))restarted.tokens.put(key,Base64.getDecoder().decode(loaded.getProperty(key)));
        connect(restarted,"",false,false,false,false);if(!"办公电脑".equals(restarted.computerName))throw new AssertionError("Renamed computer not updated");pass("client persistence and computer rename on reconnect");
        connect(new Trust(),"000000",true,false,false,false);pass("wrong PIN rejected");
        connect(new Trust(),"",true,false,false,false);pass("unknown computer requires PIN");
        try(Socket socket=new Socket("127.0.0.1",port)){
            socket.setSoTimeout(10000);String[] messages=new String(replay,StandardCharsets.UTF_8).split("\n");OutputStream output=socket.getOutputStream();output.write((messages[0]+"\n").getBytes(StandardCharsets.UTF_8));output.flush();
            InputStream input=socket.getInputStream();for(int b;(b=input.read())!=10;)if(b<0)throw new EOFException();output.write((messages[1]+"\n").getBytes(StandardCharsets.UTF_8));output.flush();if(!Wire.readLine(input).equals("NO"))throw new AssertionError("Replayed login accepted");
        }pass("replayed encrypted login rejected");
        connect(restarted,"",true,true,false,false);pass("spoofed computer proof rejected");
        connect(new Trust(),"123456",true,false,false,true);pass("tampered enrollment rejected");
        connect(restarted,"",true,false,false,false);pass("revoked phone requires PIN");
        byte[] previous=restarted.tokens.values().iterator().next().clone();connect(restarted,"123456",false,false,false,false);if(Arrays.equals(previous,restarted.tokens.values().iterator().next()))throw new AssertionError("Re-pair did not rotate credential");pass("explicit re-pair rotates credentials");
        Trust imposter=restarted.copy();imposter.id=DevicePairing.newId();connect(imposter,"",true,false,false,false);pass("credential bound to phone identity");
        if(!ConnectionTime.format(7200123).equals("02:00:00") || !ConnectionTime.format(90061000).equals("25:01:01"))throw new AssertionError("Connection duration wraps or drifts");
        pass("connection duration seconds and hours");
    }
}
