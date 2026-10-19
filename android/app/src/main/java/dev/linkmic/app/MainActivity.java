package dev.linkmic.app;

import android.Manifest;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.os.*;
import android.view.*;
import android.widget.TextView;
import android.widget.Toast;
import androidx.appcompat.app.AppCompatActivity;
import androidx.appcompat.app.AlertDialog;
import androidx.core.view.*;
import com.google.android.material.appbar.MaterialToolbar;
import com.google.android.material.button.*;
import com.google.android.material.color.DynamicColors;
import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.progressindicator.LinearProgressIndicator;
import com.google.android.material.textfield.*;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;

public final class MainActivity extends AppCompatActivity {
    private TextView status,duration,trustedState,help;
    private TextInputEditText host,code;private TextInputLayout hostLayout,codeLayout;
    private MaterialButton start,stop,bluetooth,wifi,pairNew;
    private View pairingCard;private LinearProgressIndicator meter;
    private TrustedComputers trust;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final ExecutorService storage=Executors.newSingleThreadExecutor();
    private int knownComputers;private boolean forcePairing,lastActive,managingDevices;
    private UiStatus previousStatus;
    private final Runnable refresh=new Runnable(){@Override public void run(){renderState();long delay=MicService.connectedAtElapsed==0?1000:1000-MicService.connectionDuration()%1000;main.postDelayed(this,Math.max(1,delay));}};
    private final Runnable refreshMeter=new Runnable(){@Override public void run(){int value=(int)(MicService.level*100);if(meter.getProgress()!=value)meter.setProgressCompat(value,false);main.postDelayed(this,200);}};

    @Override protected void attachBaseContext(Context base){super.attachBaseContext(AppLanguage.wrap(base));}
    @Override public void onCreate(Bundle state){
        super.onCreate(state);DynamicColors.applyToActivityIfAvailable(this);
        WindowCompat.setDecorFitsSystemWindows(getWindow(),false);setContentView(R.layout.activity_main);
        boolean light=(getResources().getConfiguration().uiMode&Configuration.UI_MODE_NIGHT_MASK)!=Configuration.UI_MODE_NIGHT_YES;
        WindowInsetsControllerCompat bars=WindowCompat.getInsetsController(getWindow(),getWindow().getDecorView());bars.setAppearanceLightStatusBars(light);bars.setAppearanceLightNavigationBars(light);
        if(Build.VERSION.SDK_INT==26)getWindow().setNavigationBarColor(android.graphics.Color.BLACK);
        View root=findViewById(R.id.screen_root);
        ViewCompat.setOnApplyWindowInsetsListener(root,(view,insets)->{androidx.core.graphics.Insets safe=insets.getInsets(WindowInsetsCompat.Type.systemBars()|WindowInsetsCompat.Type.displayCutout());androidx.core.graphics.Insets ime=insets.getInsets(WindowInsetsCompat.Type.ime());view.setPadding(safe.left,safe.top,safe.right,Math.max(safe.bottom,ime.bottom));return insets;});
        status=findViewById(R.id.connection_status);duration=findViewById(R.id.connection_duration);trustedState=findViewById(R.id.trusted_state);help=findViewById(R.id.connection_help);
        host=findViewById(R.id.computer_address);code=findViewById(R.id.pairing_code);hostLayout=findViewById(R.id.host_layout);codeLayout=findViewById(R.id.code_layout);
        start=findViewById(R.id.start_connection);stop=findViewById(R.id.stop_connection);bluetooth=findViewById(R.id.mode_bluetooth);wifi=findViewById(R.id.mode_wifi);pairNew=findViewById(R.id.pair_new_computer);
        pairingCard=findViewById(R.id.pairing_card);meter=findViewById(R.id.audio_meter);trust=new TrustedComputers(this);
        host.setText(state==null?getPreferences(0).getString("host",""):state.getString("host",""));
        forcePairing=state!=null && state.getBoolean("pairing",false);
        if(state!=null)code.setText(state.getString("code",""));
        MaterialButtonToggleGroup modes=findViewById(R.id.connection_modes);
        // During this callback the other button may not yet be unchecked.
        // The event identifies the new mode; sibling isChecked() can be stale.
        modes.addOnButtonCheckedListener((group,id,checked)->{if(checked)showMode(id==R.id.mode_wifi);});
        modes.check((state==null?getPreferences(0).getBoolean("wifi",false):state.getBoolean("wifi",false))?R.id.mode_wifi:R.id.mode_bluetooth);showMode(modes.getCheckedButtonId()==R.id.mode_wifi);
        ((MaterialToolbar)findViewById(R.id.toolbar)).setOnMenuItemClickListener(item->{if(item.getItemId()==R.id.manage_devices){manageDevices();return true;}return false;});
        pairNew.setOnClickListener(view->{forcePairing=true;updatePairingCard();});
        start.setOnClickListener(view->begin());stop.setOnClickListener(view->{MicService.status=UiStatus.message(R.string.stopped);stopService(new Intent(this,MicService.class));renderState();});
        code.setOnEditorActionListener((view,action,event)->{if(action==android.view.inputmethod.EditorInfo.IME_ACTION_DONE){begin();return true;}return false;});
    }
    private void showMode(boolean useWifi){hostLayout.setVisibility(useWifi?View.VISIBLE:View.GONE);help.setText(useWifi?R.string.wifi_help:R.string.bluetooth_help);}
    private static String value(TextInputEditText edit){return edit.getText()==null?"":edit.getText().toString().trim();}
    private static void textIfChanged(TextView view,String value){if(!view.getText().toString().equals(value))view.setText(value);}
    void renderState(){
        UiStatus current=MicService.status;boolean active=MicService.active;
        textIfChanged(status,current.text(this));textIfChanged(duration,ConnectionTime.format(MicService.connectionDuration()));
        if(current!=previousStatus){
            previousStatus=current;
            if(current.connected()){code.setText("");forcePairing=false;trustedState.setText(R.string.paired_connected);updatePairingCard();loadTrustCount();}
            else if(current.needsPairing){forcePairing=true;updatePairingCard();}
        }
        boolean canStart=!active&&!managingDevices;
        if(active!=lastActive || start.isEnabled()!=canStart){lastActive=active;start.setEnabled(canStart);stop.setEnabled(active);bluetooth.setEnabled(!active);wifi.setEnabled(!active);host.setEnabled(!active);code.setEnabled(!active);pairNew.setEnabled(!active);updatePairingCard();}
    }
    private void loadTrustCount(){
        if(storage.isShutdown())return;
        storage.execute(()->{
            try{int count=trust.count();main.post(()->{if(isDestroyed())return;knownComputers=count;textIfChanged(trustedState,count==0?getString(MicService.connectedAtElapsed!=0?R.string.paired_connected:R.string.not_paired):getString(R.string.paired_computers,count));updatePairingCard();});}
            catch(IOException error){main.post(()->{if(!isDestroyed()){trustedState.setText(R.string.trust_unavailable);forcePairing=true;updatePairingCard();}});}
        });
    }
    private void updatePairingCard(){boolean connected=MicService.connectedAtElapsed!=0;pairingCard.setVisibility(!connected&&(knownComputers==0||forcePairing)?View.VISIBLE:View.GONE);pairNew.setVisibility(!connected&&knownComputers>0&&!forcePairing?View.VISIBLE:View.GONE);}
    private void manageDevices(){
        if(MicService.active){toast(getString(R.string.disconnect_first));return;}
        if(managingDevices)return;
        managingDevices=true;renderState();
        storage.execute(()->{
            try{List<TrustedComputers.Computer> computers=trust.list();main.post(()->{
                if(isDestroyed())return;
                AlertDialog dialog=PairedComputersDialog.show(this,computers,(selected,current)->forgetComputers(selected,current));
                dialog.setOnDismissListener(ignored->{managingDevices=false;renderState();});
            });}
            catch(IOException error){main.post(()->{
                if(isDestroyed())return;
                AlertDialog dialog=new MaterialAlertDialogBuilder(this).setTitle(R.string.trust_unavailable).setMessage(R.string.reset_pairing_description)
                    .setNegativeButton(R.string.cancel,null).setPositiveButton(R.string.reset_pairing,null).create();
                dialog.setOnDismissListener(ignored->{managingDevices=false;renderState();});dialog.show();
                dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view->resetPairing(dialog));
            });}
        });
    }
    private void forgetComputers(Set<String> selected,AlertDialog dialog){
        dialog.setCancelable(false);dialog.getListView().setEnabled(false);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(false);
        storage.execute(()->{
            try{
                if(MicService.active)throw new IOException(getString(R.string.disconnect_first));
                int remaining=trust.forget(selected);
                main.post(()->{
                    if(isDestroyed())return;
                    knownComputers=remaining;forcePairing=remaining==0;code.setText("");MicService.status=UiStatus.message(R.string.forgotten_selected,selected.size());
                    dialog.dismiss();loadTrustCount();renderState();
                });
            }catch(IOException error){main.post(()->{
                if(isDestroyed())return;
                dialog.setCancelable(true);dialog.getListView().setEnabled(true);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(true);toast(getString(R.string.forget_failed));
            });}
        });
    }
    private void resetPairing(AlertDialog dialog){
        dialog.setCancelable(false);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(false);
        storage.execute(()->{trust.forgetAll();main.post(()->{if(isDestroyed())return;knownComputers=0;forcePairing=true;code.setText("");MicService.status=UiStatus.message(R.string.forgotten);dialog.dismiss();loadTrustCount();renderState();});});
    }
    private void begin(){
        if(MicService.active||managingDevices)return;
        String pin=value(code);codeLayout.setError(null);hostLayout.setError(null);
        if(!pin.isEmpty()&&!pin.matches("[0-9]{6}")){codeLayout.setError(getString(R.string.invalid_code));return;}
        if(wifi.isChecked()&&value(host).isEmpty()){hostLayout.setError(getString(R.string.missing_address));return;}
        List<String> required=new ArrayList<>();required.add(Manifest.permission.RECORD_AUDIO);
        if(Build.VERSION.SDK_INT>=31&&bluetooth.isChecked())required.add(Manifest.permission.BLUETOOTH_CONNECT);
        if(Build.VERSION.SDK_INT>=33)required.add(Manifest.permission.POST_NOTIFICATIONS);
        List<String> missing=new ArrayList<>();for(String permission:required)if(checkSelfPermission(permission)!=PackageManager.PERMISSION_GRANTED)missing.add(permission);
        if(!missing.isEmpty()){requestPermissions(missing.toArray(new String[0]),7);return;}startServiceNow();
    }
    private void startServiceNow(){
        if(MicService.active||managingDevices)return;
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED||(Build.VERSION.SDK_INT>=31&&bluetooth.isChecked()&&checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED)){toast(getString(R.string.permissions_required));return;}
        getPreferences(0).edit().putString("host",value(host)).putBoolean("wifi",wifi.isChecked()).apply();
        Intent intent=new Intent(this,MicService.class).putExtra("mode",bluetooth.isChecked()?"bluetooth":"wifi").putExtra("host",value(host)).putExtra("code",value(code));
        try{startForegroundService(intent);start.setEnabled(false);status.setText(R.string.preparing);}catch(RuntimeException error){toast(getString(R.string.error_start));}
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants){super.onRequestPermissionsResult(request,permissions,grants);if(request==7)startServiceNow();}
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putString("host",value(host));state.putString("code",value(code));state.putBoolean("wifi",wifi.isChecked());state.putBoolean("pairing",forcePairing);}
    @Override protected void onResume(){super.onResume();loadTrustCount();main.removeCallbacks(refresh);main.removeCallbacks(refreshMeter);main.post(refresh);main.post(refreshMeter);}
    @Override protected void onPause(){main.removeCallbacks(refresh);main.removeCallbacks(refreshMeter);super.onPause();}
    @Override protected void onDestroy(){main.removeCallbacksAndMessages(null);storage.shutdownNow();super.onDestroy();}
    private void toast(String message){Toast.makeText(this,message,Toast.LENGTH_LONG).show();}
}
