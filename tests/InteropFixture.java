import dev.linkmic.app.Wire;
import java.io.*;
import java.nio.*;
import java.nio.file.*;

public final class InteropFixture {
    public static void main(String[] args) throws Exception {
        Path directory=Paths.get(args[0]); Files.createDirectories(directory);
        try(OutputStream packets=Files.newOutputStream(directory.resolve("android-frames.bin"));OutputStream pcm=Files.newOutputStream(directory.resolve("reference.pcm"))){
            for(int frame=0;frame<160;frame++){
                short[] samples=new short[Wire.SAMPLES];
                for(int i=0;i<samples.length;i++){
                    double t=(frame*Wire.SAMPLES+i)/(double)Wire.RATE;
                    samples[i]=(short)(frame<5?0:10000*Math.sin(2*Math.PI*440*t)+3000*Math.sin(2*Math.PI*920*t));
                }
                packets.write(Wire.encode(samples,frame,frame*20000L,987654321L));
                ByteBuffer bytes=ByteBuffer.allocate(samples.length*2).order(ByteOrder.LITTLE_ENDIAN);for(short sample:samples)bytes.putShort(sample);pcm.write(bytes.array());
            }
        }
        System.out.println("Android Java encoder: 160 framed packets generated.");
    }
}
