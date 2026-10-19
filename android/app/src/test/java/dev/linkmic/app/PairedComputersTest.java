package dev.linkmic.app;

import android.graphics.*;
import android.view.*;
import android.widget.ListView;
import androidx.appcompat.app.AlertDialog;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;
import java.util.concurrent.*;
import org.robolectric.util.ReflectionHelpers;
import org.robolectric.shadows.ShadowDialog;
import com.google.android.material.appbar.MaterialToolbar;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=28,qualifiers="w393dp-h852dp-notnight-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class PairedComputersTest {
    private static final String PHONE="00000000000000000000000000000001",A="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",B="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",C="cccccccccccccccccccccccccccccccc";
    private static byte[] token(int value){byte[] bytes=new byte[32];Arrays.fill(bytes,(byte)value);return bytes;}
    private static final class MemoryRecords implements TrustedComputers.Records {
        String data;boolean fail,deleted;int writes;
        MemoryRecords()throws Exception{data=new JSONObject().put("phoneId",PHONE).put("computers",new JSONObject()).toString();}
        public boolean exists(){return data!=null;}
        public JSONObject load()throws IOException{try{return new JSONObject(data);}catch(JSONException error){throw new IOException(error);}}
        public void save(JSONObject state)throws IOException{if(fail)throw new IOException("Synthetic atomic write failure");data=state.toString();writes++;}
        public void delete(){deleted=true;data=null;}
    }
    @Before public void reset(){MicService.active=false;MicService.status=UiStatus.message(R.string.ready);MicService.connectedAtElapsed=MicService.completedDuration=0;}
    @Test public void legacyCredentialsGainNamesOnReconnectWithoutChangingIdentity()throws Exception{
        MemoryRecords disk=new MemoryRecords();JSONObject state=disk.load();state.getJSONObject("computers").put(A,Base64.getEncoder().encodeToString(token(1)));disk.save(state);
        TrustedComputers store=new TrustedComputers(disk);
        assertEquals("",store.list().get(0).name);assertArrayEquals(token(1),store.tokenFor(A));assertEquals(PHONE,store.phoneId());
        store.remember(A,token(1),"工作电脑");
        TrustedComputers reloaded=new TrustedComputers(disk);
        assertEquals("工作电脑",reloaded.list().get(0).name);assertArrayEquals(token(1),reloaded.tokenFor(A));assertEquals(PHONE,reloaded.phoneId());
        reloaded.remember(A,token(1),"书房电脑");assertEquals("书房电脑",store.list().get(0).name);assertEquals(1,store.count());
    }
    @Test public void selectiveForgetKeepsOtherCredentialsEvenWithDuplicateNames()throws Exception{
        MemoryRecords disk=new MemoryRecords();TrustedComputers store=new TrustedComputers(disk);
        store.remember(A,token(1),"DESKTOP");store.remember(B,token(2),"DESKTOP");store.remember(C,token(3),"笔记本");
        int writes=disk.writes;assertEquals(3,store.forget(Collections.emptySet()));assertEquals(writes,disk.writes);
        assertEquals(2,store.forget(Collections.singleton(B)));
        TrustedComputers reloaded=new TrustedComputers(disk);assertNull(reloaded.tokenFor(B));assertArrayEquals(token(1),reloaded.tokenFor(A));assertArrayEquals(token(3),reloaded.tokenFor(C));assertEquals(PHONE,reloaded.phoneId());
        assertEquals(0,reloaded.forget(new HashSet<>(Arrays.asList(A,C))));assertEquals(PHONE,reloaded.phoneId());assertFalse(disk.deleted);
    }
    @Test public void failedSelectiveForgetPreservesEveryRecord()throws Exception{
        MemoryRecords disk=new MemoryRecords();TrustedComputers store=new TrustedComputers(disk);store.remember(A,token(1),"台式机");store.remember(B,token(2),"笔记本");
        String before=disk.data;disk.fail=true;
        try{store.forget(Collections.singleton(A));fail("Write failure ignored");}catch(IOException expected){}
        assertEquals(before,disk.data);assertFalse(disk.deleted);assertEquals(2,store.count());assertArrayEquals(token(1),store.tokenFor(A));assertArrayEquals(token(2),store.tokenFor(B));
    }
    @Test public void dialogShowsNamesAndForgetsOnlyCheckedIdentity()throws Exception{
        MemoryRecords disk=new MemoryRecords();TrustedComputers store=new TrustedComputers(disk);store.remember(A,token(1),"工作电脑");store.remember(B,token(2),"工作电脑");
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            AtomicReference<Set<String>> chosen=new AtomicReference<>();
            AlertDialog dialog=PairedComputersDialog.show(controller.get(),store.list(),(ids,current)->{chosen.set(ids);try{store.forget(ids);}catch(IOException error){throw new AssertionError(error);}current.dismiss();});
            ListView list=dialog.getListView();assertEquals(2,list.getCount());assertTrue(list.getAdapter().getItem(0).toString().contains("工作电脑"));assertTrue(list.getAdapter().getItem(1).toString().contains("bbbbbbbb"));assertFalse(dialog.getButton(AlertDialog.BUTTON_POSITIVE).isEnabled());
            list.performItemClick(null,1,list.getAdapter().getItemId(1));assertTrue(dialog.getButton(AlertDialog.BUTTON_POSITIVE).isEnabled());assertTrue(dialog.getButton(AlertDialog.BUTTON_POSITIVE).getText().toString().contains("1"));
            save(dialog,"android-paired-computers.png");
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick();assertEquals(Collections.singleton(B),chosen.get());assertArrayEquals(token(1),store.tokenFor(A));assertNull(store.tokenFor(B));
        }
    }
    @Test public void singleComputerIsNamedAndClosingDoesNotForget()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            AtomicReference<Set<String>> chosen=new AtomicReference<>();
            AlertDialog single=PairedComputersDialog.show(controller.get(),Collections.singletonList(new TrustedComputers.Computer(A,"DESKTOP-STUDY")),(ids,current)->chosen.set(ids));
            assertEquals(1,single.getListView().getCount());assertTrue(single.getListView().getAdapter().getItem(0).toString().contains("DESKTOP-STUDY"));
            single.getListView().performItemClick(null,0,0);single.getListView().performItemClick(null,0,0);assertFalse(single.getButton(AlertDialog.BUTTON_POSITIVE).isEnabled());
            single.getButton(AlertDialog.BUTTON_NEGATIVE).performClick();assertNull(chosen.get());
            AlertDialog empty=PairedComputersDialog.show(controller.get(),Collections.emptyList(),(ids,current)->fail("Empty list must not forget"));assertFalse(empty.getButton(AlertDialog.BUTTON_NEGATIVE).isShown());empty.dismiss();
        }
    }
    @Test public void toolbarManagerPreservesUnselectedComputerAndRestoresIdleControls()throws Exception{
        MemoryRecords disk=new MemoryRecords();TrustedComputers store=new TrustedComputers(disk);store.remember(A,token(1),"工作电脑");store.remember(B,token(2),"笔记本");
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            MainActivity activity=controller.get();drainStorage(activity);ReflectionHelpers.setField(activity,"trust",store);
            ((MaterialToolbar)activity.findViewById(R.id.toolbar)).getMenu().performIdentifierAction(R.id.manage_devices,0);
            assertFalse(activity.findViewById(R.id.start_connection).isEnabled());drainStorage(activity);
            AlertDialog dialog=(AlertDialog)ShadowDialog.getLatestDialog();assertEquals(2,dialog.getListView().getCount());
            int index=dialog.getListView().getAdapter().getItem(0).toString().contains("bbbbbbbb")?0:1;
            dialog.getListView().performItemClick(null,index,index);dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick();drainStorage(activity);drainStorage(activity);
            assertFalse(dialog.isShowing());assertNull(store.tokenFor(B));assertArrayEquals(token(1),store.tokenFor(A));assertEquals(1,store.count());
            assertTrue(activity.findViewById(R.id.start_connection).isEnabled());assertEquals(View.GONE,activity.findViewById(R.id.pairing_card).getVisibility());
        }
    }
    @Test @Config(qualifiers="w393dp-h852dp-night-mdpi") public void renderNamedComputerListInDarkMode()throws Exception{
        try(ActivityController<MainActivity> controller=Robolectric.buildActivity(MainActivity.class).setup()){
            AlertDialog dialog=PairedComputersDialog.show(controller.get(),Arrays.asList(new TrustedComputers.Computer(A,"书房电脑"),new TrustedComputers.Computer(B,"LAPTOP-STUDY")),(ids,current)->{});
            save(dialog,"android-paired-computers-dark.png");dialog.dismiss();
        }
    }
    private static void drainStorage(MainActivity activity)throws Exception{
        ExecutorService worker=ReflectionHelpers.getField(activity,"storage");worker.submit(()->{}).get(5,TimeUnit.SECONDS);Shadows.shadowOf(android.os.Looper.getMainLooper()).idle();
    }
    private static void save(AlertDialog dialog,String name)throws Exception{
        Shadows.shadowOf(android.os.Looper.getMainLooper()).idle();
        View root=dialog.getWindow().getDecorView();root.measure(View.MeasureSpec.makeMeasureSpec(361,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(700,View.MeasureSpec.AT_MOST));root.layout(0,0,361,root.getMeasuredHeight());
        Bitmap image=Bitmap.createBitmap(361,root.getHeight(),Bitmap.Config.ARGB_8888);Canvas canvas=new Canvas(image);canvas.drawColor(dialog.getContext().getColor(R.color.md_surface));root.draw(canvas);File target=new File(System.getProperty("linkmic.artifacts"),name);
        try(OutputStream out=new FileOutputStream(target)){assertTrue(image.compress(Bitmap.CompressFormat.PNG,100,out));}image.recycle();
    }
}
