package dev.linkmic.app;

import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

public final class Wire {
    public static final int RATE=48000, SAMPLES=960, PORT=42791;
    public static final UUID SERVICE=UUID.fromString("e7509170-7e2e-4820-b9c6-d21d21e1e531");
    private static final int[] INDEX={-1,-1,-1,-1,2,4,6,8};
    private static final int[] STEP={7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767};
    private Wire() {}
    public static byte[] encode(short[] pcm, int sequence, long micros, long session) {
        if(pcm.length!=SAMPLES) throw new IllegalArgumentException("Frame size");
        byte[] payload=new byte[4+SAMPLES/2];
        int predictor=pcm[0], index=0, delta=Math.abs(pcm[1]-predictor);
        while(index<88 && STEP[index]<delta) index++;
        ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN).putShort(pcm[0]).put((byte)index).put((byte)0);
        for(int i=1;i<SAMPLES;i++) {
            int step=STEP[index], difference=pcm[i]-predictor, code=difference<0?8:0, magnitude=Math.abs(difference);
            if(magnitude>=step){code|=4; magnitude-=step;}
            if(magnitude>=(step>>1)){code|=2; magnitude-=step>>1;}
            if(magnitude>=(step>>2)) code|=1;
            int amount=(step>>3)+((code&4)!=0?step:0)+((code&2)!=0?step>>1:0)+((code&1)!=0?step>>2:0);
            predictor=Math.max(-32768,Math.min(32767,predictor+((code&8)!=0?-amount:amount)));
            index=Math.max(0,Math.min(88,index+INDEX[code&7]));
            payload[4+(i-1)/2]|=(byte)(code<<(((i-1)%2)*4));
        }
        ByteBuffer packet=ByteBuffer.allocate(32+payload.length).order(ByteOrder.LITTLE_ENDIAN);
        packet.put(new byte[]{'L','M','I','C'}).put((byte)1).put((byte)1).putShort((short)SAMPLES);
        packet.putInt(sequence).putLong(micros).putInt(RATE).putInt(payload.length).putInt((int)session).put(payload);
        return packet.array();
    }
    public static String readLine(InputStream input) throws IOException {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        while(bytes.size()<128){int b=input.read(); if(b<0) throw new AppError(AppError.Code.DISCONNECTED); if(b==10) return bytes.toString(StandardCharsets.UTF_8.name()).trim(); bytes.write(b);}
        throw new AppError(AppError.Code.BAD_PAIRING_DATA);
    }
    public static long pair(InputStream input,OutputStream output,String code,String name) throws IOException {
        String safe=name.replaceAll("[^\\p{L}\\p{N}._-]","_"); if(safe.length()>30)safe=safe.substring(0,30);
        output.write(("LMIC/1 "+code+" "+safe+"\n").getBytes(StandardCharsets.UTF_8)); output.flush();
        String reply=readLine(input); if(!reply.startsWith("OK "))throw new AppError(AppError.Code.PAIRING_MISMATCH);
        try {long session=Long.parseLong(reply.substring(3)); if(session<=0 || session>0xffffffffL) throw new NumberFormatException(); return session;}
        catch(NumberFormatException e){throw new AppError(AppError.Code.BAD_PAIRING_DATA,e);}
    }
}
