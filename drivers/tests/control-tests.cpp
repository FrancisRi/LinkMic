#include <stdio.h>
#include "LinkMicControl.h"
#include "LinkMicAbi.h"
static int forwarded;
static NTSTATUS AudioDispatch(PDEVICE_OBJECT,PIRP) { ++forwarded; return 42; }
int main() {
    DRIVER_OBJECT driver={};
    for(auto& dispatch:driver.MajorFunction) dispatch=AudioDispatch;
    assert(LinkMicControlInitialize(&driver)==STATUS_SUCCESS);
    FILE_OBJECT writer, other;
    auto send=[&](unsigned char major,FILE_OBJECT* file,void* buffer,ULONG length,ULONG ioctl=0) {
        IRP irp={}; irp.Stack.MajorFunction=major; irp.Stack.FileObject=file;
        irp.Stack.Parameters.Write.Length=length;
        irp.Stack.Parameters.DeviceIoControl={ioctl,0,length};
        irp.AssociatedIrp.SystemBuffer=buffer;
        auto status=driver.MajorFunction[major](&fakeDevice,&irp);
        assert(status==irp.IoStatus.Status); assert(fakeLockDepth==0); return irp;
    };
    assert(send(IRP_MJ_CREATE,&writer,nullptr,0).IoStatus.Status==STATUS_DEVICE_NOT_READY);
    LinkMicControlSetReady(true);
    assert(send(IRP_MJ_CREATE,&writer,nullptr,0).IoStatus.Status==STATUS_SUCCESS);
    assert(send(IRP_MJ_CREATE,&other,nullptr,0).IoStatus.Status==STATUS_SHARING_VIOLATION);
    LINKMIC_INFO info={};
    auto query=send(IRP_MJ_DEVICE_CONTROL,&writer,&info,sizeof(info),LINKMIC_IOCTL_QUERY);
    assert(query.IoStatus.Information==32 && info.Version==1 && info.RingBytes==9600 && info.FrameBytes==1920);
    unsigned char frame[LINKMIC_FRAME_BYTES], output[LINKMIC_FRAME_BYTES];
    memset(frame,0x4A,sizeof(frame));
    assert(send(IRP_MJ_WRITE,&writer,frame,sizeof(frame)-1).IoStatus.Status==STATUS_INVALID_BUFFER_SIZE);
    for(int i=0;i<100;++i) assert(send(IRP_MJ_WRITE,&writer,frame,sizeof(frame)).IoStatus.Information==sizeof(frame));
    LinkMicReadCapture(output,sizeof(output)); assert(memcmp(frame,output,sizeof(frame))==0);
    assert(send(IRP_MJ_DEVICE_CONTROL,&other,nullptr,0,LINKMIC_IOCTL_RESET).IoStatus.Status==STATUS_DEVICE_NOT_READY);
    assert(send(IRP_MJ_DEVICE_CONTROL,&writer,nullptr,0,LINKMIC_IOCTL_RESET).IoStatus.Status==STATUS_SUCCESS);
    memset(frame,0,sizeof(frame)); LinkMicReadCapture(output,sizeof(output)); assert(memcmp(frame,output,sizeof(frame))==0);
    // Stop/restart retains a valid writer, clears old PCM and rejects writes while stopped.
    LinkMicControlSetReady(false);
    assert(send(IRP_MJ_WRITE,&writer,frame,sizeof(frame)).IoStatus.Status==STATUS_DEVICE_NOT_READY);
    LinkMicControlSetReady(true);
    assert(send(IRP_MJ_WRITE,&writer,frame,sizeof(frame)).IoStatus.Status==STATUS_SUCCESS);
    // After remove, existing control handles must still be dispatched locally.
    LinkMicControlShutdown(); LinkMicControlShutdown();
    assert(fakeDeletes==1 && fakeLinkDeletes==1);
    assert(send(IRP_MJ_WRITE,&writer,frame,sizeof(frame)).IoStatus.Status==STATUS_DEVICE_NOT_READY);
    assert(send(IRP_MJ_CLEANUP,&writer,nullptr,0).IoStatus.Status==STATUS_SUCCESS);
    assert(send(IRP_MJ_CLOSE,&writer,nullptr,0).IoStatus.Status==STATUS_SUCCESS);
    assert(forwarded==0);
    DEVICE_OBJECT audio={&driver,0}; IRP irp={};
    assert(driver.MajorFunction[IRP_MJ_CREATE](&audio,&irp)==42 && forwarded==1);
    puts("PASS: real control dispatch ABI, malformed write, exclusive writer, 100 full writes, reset ownership, stop/restart, double removal, late close routing.");
}
