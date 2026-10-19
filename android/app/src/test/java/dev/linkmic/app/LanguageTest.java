package dev.linkmic.app;

import android.app.*;
import android.content.*;
import android.content.res.Configuration;
import android.graphics.*;
import android.os.*;
import android.view.*;
import android.widget.TextView;
import androidx.appcompat.app.AlertDialog;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.android.controller.*;
import org.robolectric.annotation.*;
import org.robolectric.shadows.ShadowSystemClock;
import java.io.*;
import java.time.Duration;
import java.util.*;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=35,qualifiers="en-rUS-w393dp-h852dp-notnight-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class LanguageTest {
    @Before public void reset(){MicService.active=false;MicService.status=UiStatus.message(R.string.ready);MicService.connectedAtElapsed=MicService.completedDuration=0;}
    @Test public void englishHomeAndPairingPrompt()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();assertEnglish(activity);assertEquals("Ready",((TextView)activity.findViewById(R.id.connection_status)).getText().toString());
            save(activity,"android023-english-light.png");
            MicService.status=UiStatus.error(new AppError(AppError.Code.PAIR_REQUIRED));activity.renderState();
            assertTrue(MicService.status.needsPairing);assertEquals(View.VISIBLE,activity.findViewById(R.id.pairing_card).getVisibility());
            assertTrue(((TextView)activity.findViewById(R.id.connection_status)).getText().toString().contains("six-digit"));
            AlertDialog dialog=PairedComputersDialog.show(activity,Collections.singletonList(new TrustedComputers.Computer("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","DESKTOP-STUDY")),(ids,current)->{});
            assertTrue(dialog.getListView().getAdapter().getItem(0).toString().contains("Device aaaaaaaa"));dialog.dismiss();
        }
    }
    @Test @Config(qualifiers="zh-rCN-w393dp-h852dp-notnight-mdpi") public void chineseSystemUsesChinese()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();assertEquals("开始连接",((TextView)activity.findViewById(R.id.start_connection)).getText().toString());assertEquals("准备就绪",((TextView)activity.findViewById(R.id.connection_status)).getText().toString());
            save(activity,"android023-chinese-light.png");
        }
    }
    @Test @Config(qualifiers="zh-rTW-w393dp-h852dp-notnight-mdpi") public void otherChineseRegionsUseChinese(){
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){assertEquals("zh",controller.get().getResources().getConfiguration().getLocales().get(0).getLanguage());assertEquals("开始连接",controller.get().getString(R.string.start));}
    }
    @Test @Config(qualifiers="fr-rFR-w393dp-h852dp-notnight-mdpi") public void frenchFirstChineseSecondStillUsesEnglish(){
        Context application=RuntimeEnvironment.getApplication();Configuration config=new Configuration(application.getResources().getConfiguration());config.setLocales(new LocaleList(Locale.FRANCE,Locale.SIMPLIFIED_CHINESE));application.getResources().updateConfiguration(config,application.getResources().getDisplayMetrics());
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            assertEnglish(controller.get());assertEquals("fr",application.getResources().getConfiguration().getLocales().get(0).getLanguage());
        }
    }
    @Test @Config(qualifiers="ar-rEG-w393dp-h852dp-notnight-mdpi") public void arabicSystemUsesEnglishLeftToRight(){
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();assertEnglish(activity);View root=activity.findViewById(R.id.screen_root);layout(root);assertEquals(View.LAYOUT_DIRECTION_LTR,root.getLayoutDirection());
        }
    }
    @Test public void languageChangeRetranslatesActiveSessionWithoutResettingIt(){
        ShadowSystemClock.advanceBy(Duration.ofHours(1));MicService.active=true;MicService.connectedAtElapsed=SystemClock.elapsedRealtime()-13000;MicService.status=UiStatus.message(R.string.connected_bluetooth);
        long started=MicService.connectedAtElapsed;UiStatus state=MicService.status;
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();((TextView)activity.findViewById(R.id.computer_address)).setText("192.168.1.42");assertEquals("Connected · Bluetooth",((TextView)activity.findViewById(R.id.connection_status)).getText().toString());
            RuntimeEnvironment.setQualifiers("zh-rCN-w393dp-h852dp-notnight-mdpi");controller.recreate();activity=controller.get();
            assertEquals("已连接 · 蓝牙",((TextView)activity.findViewById(R.id.connection_status)).getText().toString());assertEquals(started,MicService.connectedAtElapsed);assertSame(state,MicService.status);assertTrue(MicService.active);
            assertEquals(View.GONE,activity.findViewById(R.id.pairing_card).getVisibility());assertFalse(activity.findViewById(R.id.start_connection).isEnabled());assertEquals("192.168.1.42",((TextView)activity.findViewById(R.id.computer_address)).getText().toString());
            RuntimeEnvironment.setQualifiers("de-rDE-w393dp-h852dp-notnight-mdpi");controller.recreate();activity=controller.get();assertEnglish(activity);
            assertEquals("Connected · Bluetooth",((TextView)activity.findViewById(R.id.connection_status)).getText().toString());assertSame(state,MicService.status);assertEquals(started,MicService.connectedAtElapsed);
        }
    }
    @Test public void serviceNotificationFollowsLanguageChangesWithoutRestartingRecording(){
        ServiceController<MicService> controller=Robolectric.buildService(MicService.class).create();
        try{
            MicService service=controller.get();MicService.status=UiStatus.message(R.string.connected_wifi);Notification english=service.buildNotification();
            assertEquals("LinkMic microphone",english.extras.getCharSequence(Notification.EXTRA_TITLE).toString());assertEquals("Connected · Wi-Fi",english.extras.getCharSequence(Notification.EXTRA_TEXT).toString());assertEquals("Stop",english.actions[0].title.toString());
            MicService.active=true;RuntimeEnvironment.setQualifiers("zh-rCN-w393dp-h852dp-notnight-mdpi");service.onConfigurationChanged(RuntimeEnvironment.getApplication().getResources().getConfiguration());
            Notification chinese=service.buildNotification();assertEquals("LinkMic 麦克风",chinese.extras.getCharSequence(Notification.EXTRA_TITLE).toString());assertEquals("已连接 · Wi-Fi",chinese.extras.getCharSequence(Notification.EXTRA_TEXT).toString());assertEquals("停止",chinese.actions[0].title.toString());assertTrue(MicService.active);
        }finally{controller.destroy();}
    }
    @Test public void allErrorCodesHaveBothTranslationsAndStablePairingMeaning(){
        Context english=AppLanguage.wrap(RuntimeEnvironment.getApplication());Configuration config=new Configuration(english.getResources().getConfiguration());config.setLocale(Locale.SIMPLIFIED_CHINESE);Context chinese=english.createConfigurationContext(config);
        for(AppError.Code code:AppError.Code.values()){
            UiStatus state=UiStatus.error(new AppError(code));assertFalse(state.text(english).matches(".*[\\p{IsHan}].*"));assertTrue(state.text(chinese).matches(".*[\\p{IsHan}].*"));
        }
        assertTrue(UiStatus.error(new AppError(AppError.Code.PAIR_REQUIRED)).needsPairing);assertTrue(UiStatus.error(new AppError(AppError.Code.PAIRING_MISMATCH)).needsPairing);
        assertFalse(UiStatus.error(new AppError(AppError.Code.COMPUTER_AUTH_FAILED)).needsPairing);
        assertFalse(UiStatus.error(new IOException("private diagnostic text")).text(english).contains("private diagnostic"));
    }
    @Test @Config(qualifiers="en-rUS-w393dp-h852dp-night-mdpi") public void renderEnglishDark()throws Exception{try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){assertEnglish(controller.get());save(controller.get(),"android023-english-dark.png");}}
    private static void assertEnglish(MainActivity activity){assertEquals("Connect",activity.getString(R.string.start));assertEquals("Bluetooth",((TextView)activity.findViewById(R.id.mode_bluetooth)).getText().toString());assertEquals("en",activity.getResources().getConfiguration().getLocales().get(0).getLanguage());}
    private static void layout(View root){root.measure(View.MeasureSpec.makeMeasureSpec(393,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(852,View.MeasureSpec.EXACTLY));root.layout(0,0,393,852);}
    private static void save(MainActivity activity,String name)throws Exception{View root=activity.findViewById(R.id.screen_root);layout(root);View a=activity.findViewById(R.id.mode_bluetooth),b=activity.findViewById(R.id.mode_wifi);assertEquals(a.getTop(),b.getTop());assertEquals(a.getBottom(),b.getBottom());Bitmap image=Bitmap.createBitmap(393,852,Bitmap.Config.ARGB_8888);root.draw(new Canvas(image));try(OutputStream output=new FileOutputStream(new File(System.getProperty("linkmic.artifacts"),name))){assertTrue(image.compress(Bitmap.CompressFormat.PNG,100,output));}image.recycle();}
}
