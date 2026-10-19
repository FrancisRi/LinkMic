package dev.linkmic.app;

import android.app.*;
import android.bluetooth.*;
import android.content.*;
import android.content.res.Configuration;
import android.content.pm.ServiceInfo;
import android.content.pm.PackageManager;
import android.Manifest;
import android.media.*;
import android.media.audiofx.*;
import android.os.*;
import java.io.*;
import java.net.*;
import java.util.concurrent.*;

public final class MicService extends Service {
    public static final String ACTION_STOP="dev.linkmic.app.STOP";
    public static volatile boolean active;
    public static volatile UiStatus status=UiStatus.message(R.string.ready);
    public static volatile double level;
    public static volatile long sent, dropped;
    public static volatile long connectedAtElapsed, completedDuration;
    public static long connectionDuration(){long started=connectedAtElapsed;return started==0?completedDuration:Math.max(0,SystemClock.elapsedRealtime()-started);}
    private volatile boolean stopped;
    private volatile AudioRecord recorder;
    private volatile Closeable link, server;
    private ExecutorService executor;
    private PowerManager.WakeLock wakeLock;
    private final ArrayBlockingQueue<byte[]> queue=new ArrayBlockingQueue<>(3);
    private final Handler main=new Handler(Looper.getMainLooper());
    private Thread capture;

    @Override public int onStartCommand(Intent intent,int flags,int id){
        if(intent==null || ACTION_STOP.equals(intent.getAction())){status=UiStatus.message(R.string.stopped);stopSelf();return START_NOT_STICKY;}
        if(active)return START_NOT_STICKY;
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED){status=UiStatus.error(new AppError(AppError.Code.MIC_PERMISSION));stopSelf();return START_NOT_STICKY;}
        active=true; stopped=false; sent=dropped=0;connectedAtElapsed=completedDuration=0;level=0;
        status=UiStatus.message(R.string.preparing);
        Notification notification=buildNotification();
        try {if(Build.VERSION.SDK_INT>=30)startForeground(1,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);else startForeground(1,notification);}
        catch(SecurityException error){status=UiStatus.message(R.string.error_foreground);stopSelf();return START_NOT_STICKY;}
        wakeLock=((PowerManager)getSystemService(POWER_SERVICE)).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"LinkMic:Capture"); wakeLock.acquire(60*60*1000L);
        String mode=intent.getStringExtra("mode"), host=intent.getStringExtra("host"), code=intent.getStringExtra("code");
        executor=Executors.newFixedThreadPool(3);
        executor.execute(()->connect(mode,host,code));
        return START_NOT_STICKY;
    }

    private void connect(String mode,String host,String code){
        try {
            InputStream input; OutputStream output;
            if("bluetooth".equals(mode)) {
                if(Build.VERSION.SDK_INT>=31 && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED)throw new AppError(AppError.Code.NEARBY_PERMISSION);
                BluetoothAdapter adapter=getSystemService(BluetoothManager.class).getAdapter();
                if(adapter==null || !adapter.isEnabled())throw new AppError(AppError.Code.BLUETOOTH_DISABLED);
                updateStatus(UiStatus.message(R.string.waiting_bluetooth));
                BluetoothServerSocket listener=adapter.listenUsingRfcommWithServiceRecord("LinkMic",Wire.SERVICE); server=listener;
                BluetoothSocket socket=listener.accept(); link=socket; listener.close(); server=null;
                input=socket.getInputStream(); output=socket.getOutputStream();
            }else {
                updateStatus(UiStatus.message(R.string.connecting_computer));
                Socket socket=new Socket(); link=socket; socket.connect(new InetSocketAddress(host,Wire.PORT),10000); socket.setTcpNoDelay(true); socket.setSoTimeout(10000);
                input=socket.getInputStream();output=socket.getOutputStream();
            }
            if(stopped){close(link);return;}
            // A bounded timer closes both TCP and RFCOMM if the peer never finishes pairing.
            Closeable pairingLink=link; Runnable timeout=()->{if(status.message==R.string.pairing_progress)close(pairingLink);};
            updateStatus(UiStatus.message(R.string.pairing_progress));main.postDelayed(timeout,10000);
            long session;
            try {session=DevicePairing.pair(input,output,code,Build.MODEL,new TrustedComputers(this));} finally {main.removeCallbacks(timeout);}
            if(stopped)return;
            if(link instanceof Socket)((Socket)link).setSoTimeout(0);
            connectedAtElapsed=SystemClock.elapsedRealtime();
            updateStatus(UiStatus.message("bluetooth".equals(mode)?R.string.connected_bluetooth:R.string.connected_wifi));
            executor.execute(()->{try { while(!stopped && input.read()!=-1){} if(!stopped)fail(UiStatus.message(R.string.computer_disconnected)); }catch(IOException e){if(!stopped)fail(UiStatus.message(R.string.computer_disconnected));}});
            capture=new Thread(()->capture(session),"LinkMic-Audio");capture.start();
            while(!stopped){byte[] packet=queue.poll(200,TimeUnit.MILLISECONDS);if(packet!=null){output.write(packet);output.flush();sent++;}}
        }catch(Exception error){if(!stopped)fail(UiStatus.error(error));}
        finally { if(!stopped&&status.connected())fail(UiStatus.message(R.string.connection_ended)); }
    }

    private void capture(long session){
        NoiseSuppressor ns=null; AcousticEchoCanceler aec=null; AudioRecord audio=null;
        try {
            android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
            int minimum=AudioRecord.getMinBufferSize(Wire.RATE,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
            if(minimum<0)throw new AppError(AppError.Code.RECORDING_UNSUPPORTED);
            if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED)throw new AppError(AppError.Code.MIC_PERMISSION);
            audio=new AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION,Wire.RATE,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT,Math.max(minimum,Wire.SAMPLES*2*4));recorder=audio;
            if(audio.getState()!=AudioRecord.STATE_INITIALIZED)throw new AppError(AppError.Code.MIC_INITIALIZATION);
            if(NoiseSuppressor.isAvailable()){ns=NoiseSuppressor.create(audio.getAudioSessionId());if(ns!=null)ns.setEnabled(true);}
            if(AcousticEchoCanceler.isAvailable()){aec=AcousticEchoCanceler.create(audio.getAudioSessionId());if(aec!=null)aec.setEnabled(true);}
            audio.startRecording();int sequence=0;long start=SystemClock.elapsedRealtimeNanos();
            while(!stopped){
                short[] pcm=new short[Wire.SAMPLES];int position=0;
                while(position<pcm.length && !stopped){int count=audio.read(pcm,position,pcm.length-position,AudioRecord.READ_BLOCKING);if(count<=0)throw new AppError(AppError.Code.MIC_READ);position+=count;}
                if(stopped)break;
                double energy=0;for(short value:pcm)energy+=(double)value*value;level=Math.min(1,Math.sqrt(energy/pcm.length)/12000);
                byte[] packet=Wire.encode(pcm,sequence++,(SystemClock.elapsedRealtimeNanos()-start)/1000,session);
                if(!queue.offer(packet)){queue.poll();dropped++;queue.offer(packet);}
            }
        }catch(Exception error){if(!stopped)fail(UiStatus.error(error));}
        finally {recorder=null;if(audio!=null){try{audio.stop();}catch(IllegalStateException ignored){}audio.release();}if(ns!=null)ns.release();if(aec!=null)aec.release();}
    }
    private void fail(UiStatus message){updateStatus(message);main.post(this::stopSelf);}
    private void updateStatus(UiStatus next){status=next;main.post(()->{if(active&&!stopped)getSystemService(NotificationManager.class).notify(1,buildNotification());});}
    Notification buildNotification(){
        Context localized=AppLanguage.wrap(this);
        getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("microphone",localized.getString(R.string.notification_channel),NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,MicService.class).setAction(ACTION_STOP),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        return new Notification.Builder(localized,"microphone").setSmallIcon(R.drawable.ic_microphone).setContentTitle(localized.getString(R.string.notification_title)).setContentText(status.text(localized)).setContentIntent(open).addAction(new Notification.Action.Builder(null,localized.getString(R.string.notification_stop),stop).build()).setOngoing(true).build();
    }
    @Override public void onConfigurationChanged(Configuration config){super.onConfigurationChanged(config);if(active&&!stopped)getSystemService(NotificationManager.class).notify(1,buildNotification());}
    private static void close(Closeable closeable){if(closeable!=null)try{closeable.close();}catch(IOException ignored){}}
    @Override public void onDestroy(){
        stopped=true;completedDuration=connectionDuration();connectedAtElapsed=0;active=false;level=0;
        close(server);close(link);
        AudioRecord audio=recorder;if(audio!=null)try{audio.stop();}catch(IllegalStateException ignored){}
        if(executor!=null)executor.shutdownNow();queue.clear();
        if(wakeLock!=null && wakeLock.isHeld())wakeLock.release();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent){return null;}
}
