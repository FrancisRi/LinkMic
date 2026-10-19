package dev.linkmic.app;

import android.graphics.*;
import android.graphics.drawable.*;
import android.content.res.Configuration;
import android.os.*;
import android.text.*;
import android.view.*;
import android.widget.TextView;
import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.*;
import org.robolectric.shadows.ShadowSystemClock;
import java.io.*;
import java.time.Duration;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=28,qualifiers="zh-rCN-w393dp-h852dp-notnight-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class MainActivityTest {
    @Before public void reset(){MicService.active=false;MicService.status=UiStatus.message(R.string.ready);MicService.connectedAtElapsed=MicService.completedDuration=0;MicService.level=0;MicService.sent=MicService.dropped=0;}
    @Test public void durationReplacesFramesAndUpdatesOnlyOncePerSecond(){
        ShadowSystemClock.advanceBy(Duration.ofHours(3));MicService.active=true;MicService.status=UiStatus.message(R.string.connected_bluetooth);MicService.connectedAtElapsed=SystemClock.elapsedRealtime();
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();MicService.connectedAtElapsed=SystemClock.elapsedRealtime();activity.renderState();TextView timer=activity.findViewById(R.id.connection_duration);AtomicInteger changes=new AtomicInteger();
            timer.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){changes.incrementAndGet();}public void afterTextChanged(Editable value){}});
            shadowOf(Looper.getMainLooper()).idle();
            for(int frame=0;frame<500;frame++)MicService.sent++;
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(999));assertEquals("00:00:00",timer.getText().toString());assertEquals(0,changes.get());
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(1));assertEquals("00:00:01",timer.getText().toString());assertEquals(1,changes.get());
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(4));assertEquals("00:00:05",timer.getText().toString());assertEquals(5,changes.get());
            controller.pause();shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(5));assertEquals(5,changes.get());controller.resume();shadowOf(Looper.getMainLooper()).idle();assertEquals("00:00:10",timer.getText().toString());
        }
    }
    @Test public void materialControlsAndWifiStateSurviveRecreation(){
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();assertTrue(activity.findViewById(R.id.start_connection) instanceof MaterialButton);
            ((MaterialButton)activity.findViewById(R.id.mode_wifi)).performClick();((TextInputEditText)activity.findViewById(R.id.computer_address)).setText("192.168.1.42");((TextInputEditText)activity.findViewById(R.id.pairing_code)).setText("123456");
            controller.recreate();activity=controller.get();assertEquals(View.VISIBLE,activity.findViewById(R.id.host_layout).getVisibility());assertEquals("192.168.1.42",((TextInputEditText)activity.findViewById(R.id.computer_address)).getText().toString());assertEquals("123456",((TextInputEditText)activity.findViewById(R.id.pairing_code)).getText().toString());
        }
    }
    @Test public void switchingBackToBluetoothHidesWifiAddressAfterRecreation(){
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            controller.recreate();MainActivity activity=controller.get();
            for(int round=0;round<5;round++){
                activity.findViewById(R.id.mode_wifi).performClick();
                assertEquals("Wi-Fi address visible in round "+round,View.VISIBLE,activity.findViewById(R.id.host_layout).getVisibility());
                activity.findViewById(R.id.mode_bluetooth).performClick();
                assertTrue(((MaterialButton)activity.findViewById(R.id.mode_bluetooth)).isChecked());
                assertEquals("Bluetooth must hide Wi-Fi address in round "+round,View.GONE,activity.findViewById(R.id.host_layout).getVisibility());
                assertEquals(activity.getString(R.string.bluetooth_help),((TextView)activity.findViewById(R.id.connection_help)).getText().toString());
            }
        }
    }
    @Test public void connectionModeButtonsShareVerticalBounds()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();View root=activity.findViewById(R.id.screen_root);layout(root,393,852);
            MaterialButton bluetooth=activity.findViewById(R.id.mode_bluetooth),wifi=activity.findViewById(R.id.mode_wifi);
            File target=new File(System.getProperty("linkmic.artifacts"),"android-mode-button-bounds-sdk"+Build.VERSION.SDK_INT+".txt");
            try(Writer output=new FileWriter(target)){
                for(String family:new String[]{"sans-serif-medium","serif","monospace","sans-serif-condensed"}){
                    bluetooth.setTypeface(Typeface.create(family,Typeface.NORMAL));wifi.setTypeface(Typeface.create(family,Typeface.NORMAL));
                    for(float size:new float[]{14,18,22}){
                        bluetooth.setTextSize(size);wifi.setTextSize(size);layout(root,393,852);
                        for(MaterialButton selected:new MaterialButton[]{bluetooth,wifi,bluetooth}){
                            selected.performClick();layout(root,393,852);
                            String measured=family+" "+size+"sp selected="+selected.getText()+": bluetooth top="+bluetooth.getTop()+" bottom="+bluetooth.getBottom()+" baseline="+bluetooth.getBaseline()+"; wifi top="+wifi.getTop()+" bottom="+wifi.getBottom()+" baseline="+wifi.getBaseline();
                            output.write(measured+"\n");output.flush();
                            assertEquals(measured,bluetooth.getTop(),wifi.getTop());assertEquals(measured,bluetooth.getBottom(),wifi.getBottom());assertTrue(bluetooth.getHeight()>=48);
                        }
                    }
                }
            }
            controller.recreate();activity=controller.get();layout(activity.findViewById(R.id.screen_root),393,852);
            View restoredBluetooth=activity.findViewById(R.id.mode_bluetooth),restoredWifi=activity.findViewById(R.id.mode_wifi);
            assertEquals(restoredBluetooth.getTop(),restoredWifi.getTop());assertEquals(restoredBluetooth.getBottom(),restoredWifi.getBottom());
        }
    }
    @Test @Config(sdk=35) public void connectionModeButtonsShareVerticalBoundsOnAndroid15()throws Exception{
        connectionModeButtonsShareVerticalBounds();
    }
    @Test @Config(sdk=35) public void renderAndroid15Light()throws Exception{render("android15-material3-light.png",393,852);}
    @Test @Config(sdk=35,qualifiers="w393dp-h852dp-night-mdpi") public void renderAndroid15Dark()throws Exception{render("android15-material3-dark.png",393,852);}
    @Test public void renderLightMaterial3()throws Exception{render("android-material3-light.png",393,852);}
    @Test @Config(qualifiers="w393dp-h852dp-night-mdpi") public void renderDarkTwoHourSession()throws Exception{
        ShadowSystemClock.advanceBy(Duration.ofHours(3));MicService.active=true;MicService.status=UiStatus.message(R.string.connected_bluetooth);MicService.connectedAtElapsed=SystemClock.elapsedRealtime()-7200000;render("android-material3-dark.png",393,852);
    }
    @Test @Config(qualifiers="w360dp-h640dp-notnight-mdpi") public void compactWifiWithLargeTextKeepsControlsReachable()throws Exception{
        android.content.res.Resources resources=RuntimeEnvironment.getApplication().getResources();
        Configuration config=new Configuration(resources.getConfiguration());config.fontScale=1.3f;resources.updateConfiguration(config,resources.getDisplayMetrics());
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();activity.findViewById(R.id.mode_wifi).performClick();
            View root=activity.findViewById(R.id.screen_root);
            androidx.core.view.ViewCompat.dispatchApplyWindowInsets(root,new androidx.core.view.WindowInsetsCompat.Builder().setInsets(androidx.core.view.WindowInsetsCompat.Type.systemBars(),androidx.core.graphics.Insets.of(0,24,0,24)).build());
            layout(root,360,640);
            for(int id:new int[]{R.id.mode_bluetooth,R.id.mode_wifi}){
                TextView button=activity.findViewById(id);assertEquals("Transport name must fit at larger font size",0,button.getLayout().getEllipsisCount(0));
            }
            assertEquals(View.VISIBLE,activity.findViewById(R.id.host_layout).getVisibility());
            TextView timer=activity.findViewById(R.id.connection_duration);assertTrue(timer.getTypeface().isBold());assertEquals(0,timer.getLayout().getEllipsisCount(0));
            Rect bounds=new Rect();assertTrue(activity.findViewById(R.id.start_connection).getGlobalVisibleRect(bounds));assertTrue(bounds.bottom<=616);assertTrue(bounds.height()>=48);
            save(root,"android-material3-compact-wifi.png",360,640);
            androidx.core.widget.NestedScrollView scroll=activity.findViewById(R.id.connection_scroll);
            assertTrue("Form must scroll on a small screen",scroll.getChildAt(0).getHeight()>scroll.getHeight());
            scroll.setSmoothScrollingEnabled(false);scroll.fullScroll(View.FOCUS_DOWN);
            shadowOf(Looper.getMainLooper()).idle();
            assertTrue("Pairing input reachable after scrolling",activity.findViewById(R.id.pairing_code).getGlobalVisibleRect(bounds));
            assertTrue(bounds.top>=24 && bounds.bottom<=activity.findViewById(R.id.start_connection).getTop()+((View)activity.findViewById(R.id.start_connection).getParent()).getTop()+((View)activity.findViewById(R.id.start_connection).getParent().getParent()).getTop());
            save(root,"android-material3-compact-scrolled.png",360,640);
        }
    }
    @Test public void launcherGlyphHasMoreRoomInsideAdaptiveMask()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();AdaptiveIconDrawable icon=(AdaptiveIconDrawable)activity.getDrawable(R.mipmap.ic_launcher);
            AdaptiveIconDrawable previous=new AdaptiveIconDrawable(new ColorDrawable(activity.getColor(R.color.icon_background)),activity.getDrawable(R.drawable.ic_microphone));
            Bitmap oldImage=Bitmap.createBitmap(108,108,Bitmap.Config.ARGB_8888),newImage=Bitmap.createBitmap(108,108,Bitmap.Config.ARGB_8888);
            previous.setBounds(0,0,108,108);previous.draw(new Canvas(oldImage));icon.setBounds(0,0,108,108);icon.draw(new Canvas(newImage));
            int oldWhite=0,newWhite=0;
            for(int y=0;y<108;y++)for(int x=0;x<108;x++){if(oldImage.getPixel(x,y)==Color.WHITE)oldWhite++;if(newImage.getPixel(x,y)==Color.WHITE)newWhite++;}
            assertTrue("Launcher glyph shrinks without disappearing",newWhite>oldWhite*0.5 && newWhite<oldWhite*0.8);
            Bitmap preview=Bitmap.createBitmap(256,144,Bitmap.Config.ARGB_8888);Canvas canvas=new Canvas(preview);canvas.drawColor(activity.getColor(R.color.md_surface));
            canvas.drawBitmap(oldImage,12,18,null);canvas.drawBitmap(newImage,136,18,null);
            write(preview,"android-icon-021-comparison.png");oldImage.recycle();newImage.recycle();preview.recycle();
        }
    }
    private static void layout(View root,int width,int height){root.measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));root.layout(0,0,width,height);}
    private static void write(Bitmap image,String name)throws Exception{File file=new File(System.getProperty("linkmic.artifacts"),name);file.getParentFile().mkdirs();try(OutputStream output=new FileOutputStream(file)){assertTrue(image.compress(Bitmap.CompressFormat.PNG,100,output));}}
    private static void save(View root,String name,int width,int height)throws Exception{Bitmap image=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888);root.draw(new Canvas(image));write(image,name);image.recycle();}
    private void render(String name,int width,int height)throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();shadowOf(Looper.getMainLooper()).idle();View root=activity.findViewById(R.id.screen_root);
            layout(root,width,height);
            assertTrue(activity.findViewById(R.id.start_connection).isShown());
            Bitmap image=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888);root.draw(new Canvas(image));
            File file=new File(System.getProperty("linkmic.artifacts"),name);file.getParentFile().mkdirs();try(OutputStream output=new FileOutputStream(file)){assertTrue(image.compress(Bitmap.CompressFormat.PNG,100,output));}image.recycle();
        }
    }
}
