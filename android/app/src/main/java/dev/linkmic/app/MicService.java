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
    private volatile boolean ending, disconnectRequested, peerSupportsDisconnect;
    private static final int DISCONNECT_GRACE_MS=750;
    private volatile AudioRecord recorder;
    private volatile Closeable link, server;
    private ExecutorService executor;
    private PowerManager.WakeLock wakeLock;
    private final ArrayBlockingQueue<byte[]> queue=new ArrayBlockingQueue<>(3);
    private final Handler main=new Handler(Looper.getMainLooper());
    private Thread capture;

    @Override public int onStartCommand(Intent intent,int flags,int id){
        if(intent==null){stopSelf();return START_NOT_STICKY;}
        if(ACTION_STOP.equals(intent.getAction())){if(active)requestDisconnect();else stopSelf();return START_NOT_STICKY;}
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
                if(!running()){close(listener);return;}
                BluetoothSocket socket=listener.accept(); link=socket; listener.close(); server=null;
                input=socket.getInputStream(); output=socket.getOutputStream();
            }else {
                updateStatus(UiStatus.message(R.string.connecting_computer));
                Socket socket=new Socket(); link=socket;
                if(!running()){close(socket);return;}
                socket.connect(new InetSocketAddress(host,Wire.PORT),10000); socket.setTcpNoDelay(true); socket.setSoTimeout(10000);
                input=socket.getInputStream();output=socket.getOutputStream();
            }
            if(!running()){close(link);return;}
            // A bounded timer closes both TCP and RFCOMM if the peer never finishes pairing.
            Closeable pairingLink=link; Runnable timeout=()->{if(status.message==R.string.pairing_progress)close(pairingLink);};
            updateStatus(UiStatus.message(R.string.pairing_progress));main.postDelayed(timeout,10000);
            long session;
            try {session=DevicePairing.pair(input,output,code,Build.MODEL,new TrustedComputers(this));} finally {main.removeCallbacks(timeout);}
            if(link instanceof Socket)((Socket)link).setSoTimeout(0);
            synchronized(this){
                if(!running())return;
                connectedAtElapsed=SystemClock.elapsedRealtime();
                updateStatus(UiStatus.message("bluetooth".equals(mode)?R.string.connected_bluetooth:R.string.connected_wifi));
            }
            executor.execute(()->watchPeer(input));
            capture=new Thread(()->capture(session),"LinkMic-Audio");capture.start();
            sendAudio(output);
        }catch(Exception error){finish(UiStatus.error(error));}
    }

    private boolean running(){return !stopped&&!ending&&!disconnectRequested;}

    void watchPeer(InputStream input){
        try{
            while(!stopped&&!ending){
                int signal=input.read();
                if(signal==Wire.SESSION_READY)peerSupportsDisconnect=true;
                else if(signal==Wire.SESSION_DISCONNECT){finish(UiStatus.message(R.string.connection_disconnected));return;}
                else if(signal<0){finish(UiStatus.message(peerSupportsDisconnect?R.string.connection_lost:R.string.connection_disconnected));return;}
            }
        }catch(IOException error){finish(UiStatus.message(R.string.connection_lost));}
    }

    void sendAudio(OutputStream output)throws InterruptedException{
        boolean readySent=false;
        try{
            while(running()){
                byte[] packet=queue.poll(200,TimeUnit.MILLISECONDS);
                if(!running())break;
                if(peerSupportsDisconnect&&!readySent){Wire.sendControl(output,Wire.SESSION_READY);readySent=true;}
                if(packet!=null){output.write(packet);output.flush();sent++;}
            }
            if(!stopped&&!ending&&disconnectRequested){
                if(peerSupportsDisconnect){
                    if(!readySent)Wire.sendControl(output,Wire.SESSION_READY);
                    Wire.sendControl(output,Wire.SESSION_DISCONNECT);
                }else finish(UiStatus.message(R.string.connection_disconnected));
            }
        }catch(IOException error){
            // A closing peer can stop accepting audio before its final signal is read.
            main.postDelayed(()->finish(UiStatus.message(R.string.connection_lost)),DISCONNECT_GRACE_MS);
        }
    }

    private void capture(long session){
        NoiseSuppressor ns=null; AcousticEchoCanceler aec=null; AudioRecord audio=null;
        try {
            if(!running())return;
            android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
            int minimum=AudioRecord.getMinBufferSize(Wire.RATE,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
            if(minimum<0)throw new AppError(AppError.Code.RECORDING_UNSUPPORTED);
            if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED)throw new AppError(AppError.Code.MIC_PERMISSION);
            audio=new AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION,Wire.RATE,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT,Math.max(minimum,Wire.SAMPLES*2*4));recorder=audio;
            if(audio.getState()!=AudioRecord.STATE_INITIALIZED)throw new AppError(AppError.Code.MIC_INITIALIZATION);
            if(NoiseSuppressor.isAvailable()){ns=NoiseSuppressor.create(audio.getAudioSessionId());if(ns!=null)ns.setEnabled(true);}
            if(AcousticEchoCanceler.isAvailable()){aec=AcousticEchoCanceler.create(audio.getAudioSessionId());if(aec!=null)aec.setEnabled(true);}
            audio.startRecording();int sequence=0;long start=SystemClock.elapsedRealtimeNanos();
            while(running()){
                short[] pcm=new short[Wire.SAMPLES];int position=0;
                while(position<pcm.length && running()){int count=audio.read(pcm,position,pcm.length-position,AudioRecord.READ_BLOCKING);if(count<=0)throw new AppError(AppError.Code.MIC_READ);position+=count;}
                if(!running())break;
                double energy=0;for(short value:pcm)energy+=(double)value*value;level=Math.min(1,Math.sqrt(energy/pcm.length)/12000);
                byte[] packet=Wire.encode(pcm,sequence++,(SystemClock.elapsedRealtimeNanos()-start)/1000,session);
                if(!queue.offer(packet)){queue.poll();dropped++;queue.offer(packet);}
            }
        }catch(Exception error){if(running())finish(UiStatus.error(error));}
        finally {recorder=null;if(audio!=null){try{audio.stop();}catch(IllegalStateException ignored){}audio.release();}if(ns!=null)ns.release();if(aec!=null)aec.release();}
    }
    private synchronized void requestDisconnect(){
        if(!running())return;
        disconnectRequested=true;
        UiStatus disconnected=UiStatus.message(R.string.connection_disconnected);
        publishStatus(disconnected);
        if(connectedAtElapsed==0)finish(disconnected);
        else main.postDelayed(()->finish(disconnected),DISCONNECT_GRACE_MS);
    }
    private synchronized void finish(UiStatus message){
        if(stopped||ending)return;
        ending=true;
        publishStatus(disconnectRequested?UiStatus.message(R.string.connection_disconnected):message);
        main.post(this::stopSelf);
    }
    private synchronized void updateStatus(UiStatus next){if(running())publishStatus(next);}
    private void publishStatus(UiStatus next){status=next;main.post(()->{if(active&&!stopped)getSystemService(NotificationManager.class).notify(1,buildNotification());});}
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
        synchronized(this){stopped=true;completedDuration=connectionDuration();connectedAtElapsed=0;active=false;level=0;}
        main.removeCallbacksAndMessages(null);
        close(server);close(link);
        AudioRecord audio=recorder;if(audio!=null)try{audio.stop();}catch(IllegalStateException ignored){}
        if(executor!=null)executor.shutdownNow();queue.clear();
        if(wakeLock!=null && wakeLock.isHeld())wakeLock.release();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent){return null;}
}
