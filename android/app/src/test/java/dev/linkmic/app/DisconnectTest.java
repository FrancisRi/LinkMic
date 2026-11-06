package dev.linkmic.app;

import android.content.Intent;
import android.os.Looper;
import android.os.SystemClock;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.android.controller.ServiceController;
import org.robolectric.annotation.Config;
import java.io.*;
import java.time.Duration;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=35,qualifiers="zh-rCN")
public class DisconnectTest {
    private ServiceController<MicService> controller;
    private MicService service;

    @Before public void connect(){
        controller=Robolectric.buildService(MicService.class).create();
        service=controller.get();
        MicService.active=true;
        MicService.connectedAtElapsed=Math.max(1,SystemClock.elapsedRealtime());
        MicService.status=UiStatus.message(R.string.connected_bluetooth);
    }

    @After public void destroy(){controller.destroy();}

    @Test public void desktopDisconnectWinsOverAudioWriteFailure(){
        service.watchPeer(new InputStream(){
            int step;
            @Override public int read(){
                if(step++==0)return Wire.SESSION_READY;
                try{
                    service.sendAudio(new OutputStream(){@Override public void write(int value)throws IOException{throw new IOException("broken pipe");}});
                }catch(InterruptedException error){throw new AssertionError(error);}
                return Wire.SESSION_DISCONNECT;
            }
        });
        assertEquals(R.string.connection_disconnected,MicService.status.message);
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(1));
        assertEquals(R.string.connection_disconnected,MicService.status.message);
        assertEquals("已断开连接",MicService.status.text(AppLanguage.wrap(service)));
    }

    @Test public void phoneStopSendsControlBeforeClosing(){
        ByteArrayOutputStream output=new ByteArrayOutputStream();
        service.watchPeer(new InputStream(){
            int step;
            @Override public int read(){
                if(step++==0)return Wire.SESSION_READY;
                service.onStartCommand(new Intent(service,MicService.class).setAction(MicService.ACTION_STOP),0,1);
                try{service.sendAudio(output);}catch(InterruptedException error){throw new AssertionError(error);}
                return -1;
            }
        });
        assertArrayEquals(new byte[]{Wire.SESSION_READY,Wire.SESSION_DISCONNECT},output.toByteArray());
        assertEquals(R.string.connection_disconnected,MicService.status.message);
    }

    @Test public void legacyEofStillMeansDisconnected(){
        service.watchPeer(new ByteArrayInputStream(new byte[0]));
        assertEquals(R.string.connection_disconnected,MicService.status.message);
    }

    @Test public void capablePeerEofWithoutSignalMeansConnectionLost(){
        service.watchPeer(new ByteArrayInputStream(new byte[]{Wire.SESSION_READY}));
        assertEquals(R.string.connection_lost,MicService.status.message);
    }
}
