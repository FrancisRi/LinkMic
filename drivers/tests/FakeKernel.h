// Host-only kernel API boundary for exercising the real control dispatch source.
// No device, service, registry, or driver installation occurs in this fixture.
#pragma once
#include <assert.h>
#include <stddef.h>
#include <string.h>
using NTSTATUS = long;
using ULONG = unsigned long;
using ULONG_PTR = size_t;
using KIRQL = unsigned char;
using KSPIN_LOCK = int;
constexpr NTSTATUS STATUS_SUCCESS=0, STATUS_INVALID_DEVICE_REQUEST=-1,
    STATUS_DEVICE_NOT_READY=-2, STATUS_SHARING_VIOLATION=-3,
    STATUS_INVALID_BUFFER_SIZE=-4, STATUS_BUFFER_TOO_SMALL=-5, STATUS_INVALID_PARAMETER=-6;
#define NT_SUCCESS(s) ((s)>=0)
#define IRP_MJ_MAXIMUM_FUNCTION 27
#define IRP_MJ_CREATE 0
#define IRP_MJ_CLOSE 2
#define IRP_MJ_WRITE 4
#define IRP_MJ_DEVICE_CONTROL 14
#define IRP_MJ_CLEANUP 18
#define IO_NO_INCREMENT 0
#define FILE_DEVICE_UNKNOWN 0x22
#define FILE_DEVICE_SECURE_OPEN 0x100
#define DO_BUFFERED_IO 4
#define DO_DEVICE_INITIALIZING 128
#define TRUE 1
#define RTL_CONSTANT_STRING(value) {value}
struct GUID { unsigned long a; unsigned short b,c; unsigned char d[8]; };
struct UNICODE_STRING { const wchar_t* text; };
struct FILE_OBJECT {};
using PFILE_OBJECT=FILE_OBJECT*;
struct IO_STACK_LOCATION {
    unsigned char MajorFunction;
    PFILE_OBJECT FileObject;
    struct { struct { ULONG Length; } Write; struct { ULONG IoControlCode,InputBufferLength,OutputBufferLength; } DeviceIoControl; } Parameters;
};
struct IRP {
    IO_STACK_LOCATION Stack;
    struct { NTSTATUS Status; ULONG_PTR Information; } IoStatus;
    struct { void* SystemBuffer; } AssociatedIrp;
};
using PIRP=IRP*;
struct DRIVER_OBJECT;
struct DEVICE_OBJECT { DRIVER_OBJECT* DriverObject; ULONG Flags; };
using PDEVICE_OBJECT=DEVICE_OBJECT*;
using PDRIVER_DISPATCH=NTSTATUS(*)(PDEVICE_OBJECT,PIRP);
struct DRIVER_OBJECT { PDRIVER_DISPATCH MajorFunction[IRP_MJ_MAXIMUM_FUNCTION+1]; };
using PDRIVER_OBJECT=DRIVER_OBJECT*;
inline DEVICE_OBJECT fakeDevice;
inline int fakeDeletes, fakeCompletions, fakeLinkDeletes, fakeLockDepth;
inline IO_STACK_LOCATION* IoGetCurrentIrpStackLocation(PIRP irp) { return &irp->Stack; }
inline void IoCompleteRequest(PIRP,int) { ++fakeCompletions; }
inline void KeInitializeSpinLock(KSPIN_LOCK* lock) { *lock=0; }
inline void KeAcquireSpinLock(KSPIN_LOCK*,KIRQL* old) { *old=0; ++fakeLockDepth; }
inline void KeReleaseSpinLock(KSPIN_LOCK*,KIRQL) { --fakeLockDepth; assert(fakeLockDepth==0); }
inline void RtlCopyMemory(void* out,const void* in,size_t size) { memcpy(out,in,size); }
inline NTSTATUS IoCreateDeviceSecure(PDRIVER_OBJECT driver,int,UNICODE_STRING*,int,int,int,UNICODE_STRING*,const GUID*,PDEVICE_OBJECT* device) {
    fakeDevice={driver,DO_DEVICE_INITIALIZING}; *device=&fakeDevice; return STATUS_SUCCESS;
}
inline NTSTATUS IoCreateSymbolicLink(UNICODE_STRING*,UNICODE_STRING*) { return STATUS_SUCCESS; }
inline void IoDeleteSymbolicLink(UNICODE_STRING*) { ++fakeLinkDeletes; }
inline void IoDeleteDevice(PDEVICE_OBJECT) { ++fakeDeletes; }
