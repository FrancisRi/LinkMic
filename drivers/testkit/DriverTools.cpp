#define NOMINMAX
#include <windows.h>
#include <initguid.h>
#include <setupapi.h>
#include <newdev.h>
#include <cfgmgr32.h>
#include <devguid.h>
#include <devpkey.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <sstream>
#include <string>
#include <thread>
#include <vector>
#include "LinkMicAbi.h"
using Microsoft::WRL::ComPtr;
static constexpr wchar_t Hardware[] = L"ROOT\\LinkMicCapture";
static void Check(bool ok, const char* action) { if (!ok) throw std::runtime_error(std::string(action) + " Win32=" + std::to_string(GetLastError())); }
static void Hr(HRESULT hr, const char* action) { if (FAILED(hr)) {std::ostringstream message;message<<action<<" HRESULT=0x"<<std::hex<<static_cast<unsigned long>(hr);throw std::runtime_error(message.str());} }
struct Handle { HANDLE value = INVALID_HANDLE_VALUE; ~Handle(){if(value != INVALID_HANDLE_VALUE && value != nullptr) CloseHandle(value);} };
struct Devices { HDEVINFO value; ~Devices(){if(value != INVALID_HANDLE_VALUE) SetupDiDestroyDeviceInfoList(value);} };
static std::string Utf8(const std::wstring& value) {
    int count = WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    std::string result(static_cast<size_t>(count),'\0');
    WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),result.data(),count,nullptr,nullptr); return result;
}
static std::string Json(const std::wstring& value) {
    std::string result="\""; for(unsigned char c:Utf8(value)){if(c=='\\'||c=='"')result+='\\';if(c>=32)result+=static_cast<char>(c);} return result+'"';
}
static std::wstring Property(HDEVINFO set, SP_DEVINFO_DATA& device, const DEVPROPKEY& key) {
    wchar_t data[2048]{}; DEVPROPTYPE type=0;
    if(!SetupDiGetDevicePropertyW(set,&device,&key,&type,reinterpret_cast<BYTE*>(data),sizeof(data),nullptr,0)||type!=DEVPROP_TYPE_STRING)return L"";
    return data;
}
static bool IsOurDevice(HDEVINFO set, SP_DEVINFO_DATA& device) {
    wchar_t ids[4096]{}; DWORD type=0;
    if(!SetupDiGetDeviceRegistryPropertyW(set,&device,SPDRP_HARDWAREID,&type,reinterpret_cast<BYTE*>(ids),sizeof(ids),nullptr)||type!=REG_MULTI_SZ)return false;
    for(const wchar_t* id=ids;*id;id+=wcslen(id)+1) if(_wcsicmp(id,Hardware)==0)return true;
    return false;
}
static void Remove(HDEVINFO set, SP_DEVINFO_DATA& device) {
    SP_REMOVEDEVICE_PARAMS parameters{}; parameters.ClassInstallHeader.cbSize=sizeof(SP_CLASSINSTALL_HEADER);
    parameters.ClassInstallHeader.InstallFunction=DIF_REMOVE; parameters.Scope=DI_REMOVEDEVICE_GLOBAL;
    Check(SetupDiSetClassInstallParamsW(set,&device,&parameters.ClassInstallHeader,sizeof(parameters))!=FALSE,"remove parameters");
    Check(SetupDiCallClassInstaller(DIF_REMOVE,set,&device)!=FALSE,"remove LinkMic device");
}
static int Nodes(const std::wstring& mode) {
    Devices set{SetupDiGetClassDevsW(&GUID_DEVCLASS_MEDIA,nullptr,nullptr,0)};
    Check(set.value!=INVALID_HANDLE_VALUE,"enumerate MEDIA devices"); bool comma=false; int count=0;
    std::vector<SP_DEVINFO_DATA> selected;
    for(DWORD index=0;;++index){
        SP_DEVINFO_DATA device{};device.cbSize=sizeof(device);
        if(!SetupDiEnumDeviceInfo(set.value,index,&device)){Check(GetLastError()==ERROR_NO_MORE_ITEMS,"enumerate device");break;}
        if(!IsOurDevice(set.value,device))continue;
        selected.push_back(device);
    }
    std::cout<<"[";
    for(auto device:selected){
        wchar_t instance[2048]{};Check(SetupDiGetDeviceInstanceIdW(set.value,&device,instance,2048,nullptr)!=FALSE,"instance ID");
        ULONG status=0,problem=0;CONFIGRET state=CM_Get_DevNode_Status(&status,&problem,device.DevInst,0);
        if(comma)std::cout<<",";comma=true;++count;
        std::cout<<"{\"instanceId\":"<<Json(instance)<<",\"inf\":"<<Json(Property(set.value,device,DEVPKEY_Device_DriverInfPath))
            <<",\"started\":"<<((state==CR_SUCCESS&&(status&DN_STARTED))?"true":"false")<<",\"problem\":"<<problem<<"}";
        if(mode==L"remove")Remove(set.value,device);
    }
    std::cout<<"]\n";return count;
}
static void Install(const wchar_t* path) {
    wchar_t full[MAX_PATH]{};DWORD length=GetFullPathNameW(path,MAX_PATH,full,nullptr);Check(length>0&&length<MAX_PATH,"INF path");
    Devices existing{SetupDiGetClassDevsW(&GUID_DEVCLASS_MEDIA,nullptr,nullptr,0)};Check(existing.value!=INVALID_HANDLE_VALUE,"existing devices");
    int count=0;for(DWORD index=0;;++index){SP_DEVINFO_DATA d{};d.cbSize=sizeof(d);if(!SetupDiEnumDeviceInfo(existing.value,index,&d)){Check(GetLastError()==ERROR_NO_MORE_ITEMS,"existing enumeration");break;}if(IsOurDevice(existing.value,d))++count;}
    if(count>1)throw std::runtime_error("Multiple LinkMic root devices exist; remove duplicates before installation.");
    Devices created{INVALID_HANDLE_VALUE};SP_DEVINFO_DATA device{};device.cbSize=sizeof(device);bool registered=false;
    if(count==0){
        created.value=SetupDiCreateDeviceInfoList(&GUID_DEVCLASS_MEDIA,nullptr);Check(created.value!=INVALID_HANDLE_VALUE,"device info list");
        Check(SetupDiCreateDeviceInfoW(created.value,L"LinkMicCapture",&GUID_DEVCLASS_MEDIA,L"LinkMic Virtual Microphone",nullptr,DICD_GENERATE_ID,&device)!=FALSE,"create root device");
        wchar_t ids[]=L"ROOT\\LinkMicCapture\0";
        Check(SetupDiSetDeviceRegistryPropertyW(created.value,&device,SPDRP_HARDWAREID,reinterpret_cast<BYTE*>(ids),sizeof(ids))!=FALSE,"hardware ID");
        Check(SetupDiCallClassInstaller(DIF_REGISTERDEVICE,created.value,&device)!=FALSE,"register root device");registered=true;
    }
    BOOL reboot=FALSE;
    if(!UpdateDriverForPlugAndPlayDevicesW(nullptr,Hardware,full,INSTALLFLAG_FORCE|INSTALLFLAG_NONINTERACTIVE,&reboot)){
        DWORD error=GetLastError();if(registered){try{Remove(created.value,device);}catch(...){}}
        SetLastError(error);Check(false,"install signed LinkMic INF");
    }
    std::cout<<"{\"installed\":true,\"rebootRequired\":"<<(reboot?"true":"false")<<"}\n";
}
struct Capture {
    ComPtr<IAudioClient> client;ComPtr<IAudioCaptureClient> reader;Handle event;std::vector<short> samples;unsigned int discontinuities=0;
    Capture(){
        ComPtr<IMMDeviceEnumerator> enumerator;Hr(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)),"MMDeviceEnumerator");
        ComPtr<IMMDeviceCollection> endpoints;Hr(enumerator->EnumAudioEndpoints(eCapture,DEVICE_STATE_ACTIVE,&endpoints),"capture endpoints");
        UINT count=0;Hr(endpoints->GetCount(&count),"endpoint count");ComPtr<IMMDevice> selected;
        for(UINT i=0;i<count;++i){ComPtr<IMMDevice> device;Hr(endpoints->Item(i,&device),"endpoint item");ComPtr<IPropertyStore> store;Hr(device->OpenPropertyStore(STGM_READ,&store),"endpoint properties");
            PROPVARIANT value{};PropVariantInit(&value);HRESULT hr=store->GetValue(PKEY_Device_FriendlyName,&value);
            bool matches=SUCCEEDED(hr)&&value.vt==VT_LPWSTR&&value.pwszVal&&wcsstr(value.pwszVal,L"LinkMic")!=nullptr;PropVariantClear(&value);
            if(matches){if(selected)throw std::runtime_error("Multiple LinkMic capture endpoints; test refuses an ambiguous target.");selected=device;}}
        if(!selected)throw std::runtime_error("No active LinkMic capture endpoint. No default microphone was opened.");
        Hr(selected->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(client.GetAddressOf())),"activate LinkMic WASAPI");
        WAVEFORMATEX format{};format.wFormatTag=WAVE_FORMAT_PCM;format.nChannels=1;format.nSamplesPerSec=48000;format.wBitsPerSample=16;format.nBlockAlign=2;format.nAvgBytesPerSec=96000;
        Hr(client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_EVENTCALLBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,1000000,0,&format,nullptr),"WASAPI initialize 48k mono PCM16");
        event.value=CreateEventW(nullptr,FALSE,FALSE,nullptr);Check(event.value!=nullptr,"capture event");
        Hr(client->SetEventHandle(event.value),"capture event handle");Hr(client->GetService(IID_PPV_ARGS(&reader)),"capture client");Hr(client->Start(),"start LinkMic capture");
    }
    ~Capture(){if(client)client->Stop();}
    void Read(unsigned int milliseconds){
        auto until=std::chrono::steady_clock::now()+std::chrono::milliseconds(milliseconds);
        while(std::chrono::steady_clock::now()<until){WaitForSingleObject(event.value,25);UINT available=0;Hr(reader->GetNextPacketSize(&available),"packet size");
            while(available){BYTE* data=nullptr;UINT frames=0;DWORD flags=0;Hr(reader->GetBuffer(&data,&frames,&flags,nullptr,nullptr),"capture buffer");
                if(flags&AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY)++discontinuities;
                size_t offset=samples.size();samples.resize(offset+frames);
                if(!(flags&AUDCLNT_BUFFERFLAGS_SILENT)&&data)memcpy(samples.data()+offset,data,static_cast<size_t>(frames)*2);
                Hr(reader->ReleaseBuffer(frames),"release capture buffer");Hr(reader->GetNextPacketSize(&available),"next packet");}}
    }
};
static double ToneScore(const std::vector<short>& data,size_t begin,size_t end){
    if(end<=begin)return 0;double sine=0,cosine=0,energy=0;
    for(size_t i=begin;i<end;++i){double angle=6.283185307179586*440.0*static_cast<double>(i)/48000.0;double value=data[i];sine+=value*std::sin(angle);cosine+=value*std::cos(angle);energy+=value*value;}
    return energy==0?0:2*(sine*sine+cosine*cosine)/(static_cast<double>(end-begin)*energy);
}
static void Wav(const std::wstring& path,const std::vector<short>& samples){
    std::ofstream file(path,std::ios::binary);if(!file)throw std::runtime_error("Cannot write test WAV");
    auto word=[&](unsigned short x){file.put(static_cast<char>(x));file.put(static_cast<char>(x>>8));};
    auto dword=[&](unsigned int x){word(static_cast<unsigned short>(x));word(static_cast<unsigned short>(x>>16));};
    file.write("RIFF",4);dword(36+static_cast<unsigned int>(samples.size()*2));file.write("WAVEfmt ",8);dword(16);word(1);word(1);dword(48000);dword(96000);word(2);word(16);file.write("data",4);dword(static_cast<unsigned int>(samples.size()*2));
    file.write(reinterpret_cast<const char*>(samples.data()),static_cast<std::streamsize>(samples.size()*2));if(!file)throw std::runtime_error("WAV write failed");
}
static int Audio(const std::wstring& mode,const std::wstring& output){
    Handle input;std::vector<short> tone(960);
    if(mode==L"tone"){
        input.value=CreateFileW(LINKMIC_DEVICE_PATH,GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);Check(input.value!=INVALID_HANDLE_VALUE,"open LinkMic injection; close LinkMic app first");
        LINKMIC_INFO info{};DWORD returned=0;Check(DeviceIoControl(input.value,LINKMIC_IOCTL_QUERY,nullptr,0,&info,sizeof(info),&returned,nullptr)!=FALSE,"driver QUERY");
        if(returned!=sizeof(info)||info.Size!=32||info.Version!=1||info.SampleRate!=48000||info.Channels!=1||info.BitsPerSample!=16||info.FrameBytes!=1920)throw std::runtime_error("Driver ABI mismatch");
        Check(DeviceIoControl(input.value,LINKMIC_IOCTL_RESET,nullptr,0,nullptr,0,&returned,nullptr)!=FALSE,"driver RESET");
    }
    Capture capture;std::atomic<bool> cancel=false;std::string writerError;
    std::thread writer;
    if(mode==L"tone")writer=std::thread([&]{try{
        auto next=std::chrono::steady_clock::now();for(unsigned int frame=0;frame<150&&!cancel;++frame){
            for(unsigned int sample=0;sample<960;++sample)tone[sample]=static_cast<short>(8000*std::sin(6.283185307179586*440*(frame*960.0+sample)/48000));
            DWORD written=0;Check(WriteFile(input.value,tone.data(),1920,&written,nullptr)!=FALSE&&written==1920,"complete PCM write");next+=std::chrono::milliseconds(20);std::this_thread::sleep_until(next);}
    }catch(const std::exception& e){writerError=e.what();}});
    try{capture.Read(mode==L"tone"?4000:8000);}catch(...){cancel=true;if(writer.joinable())writer.join();throw;}
    cancel=true;if(writer.joinable())writer.join();if(!writerError.empty())throw std::runtime_error(writerError);
    Wav(output,capture.samples);
    double score=ToneScore(capture.samples,24000,std::min<size_t>(capture.samples.size(),120000));
    int tailPeak=0;if(mode==L"tone"&&capture.samples.size()>24000)for(size_t i=capture.samples.size()-24000;i<capture.samples.size();++i)tailPeak=std::max(tailPeak,std::abs(static_cast<int>(capture.samples[i])));
    double energy=0;int peak=0;for(short sample:capture.samples){energy+=static_cast<double>(sample)*sample;peak=std::max(peak,std::abs(static_cast<int>(sample)));}
    double rms=capture.samples.empty()?0:std::sqrt(energy/static_cast<double>(capture.samples.size()));
    bool passed=mode==L"capture"?(capture.samples.size()>=48000&&rms>50):(capture.samples.size()>=144000&&score>.6&&tailPeak<150);
    std::cout<<"{\"passed\":"<<(passed?"true":"false")<<",\"mode\":"<<Json(mode)<<",\"samples\":"<<capture.samples.size()<<",\"rms\":"<<rms<<",\"peak\":"<<peak<<",\"toneScore\":"<<score<<",\"tailPeak\":"<<tailPeak<<",\"discontinuities\":"<<capture.discontinuities<<",\"wav\":"<<Json(output)<<"}\n";
    return passed?0:2;
}
int wmain(int argc,wchar_t** argv){
    HRESULT initialized=CoInitializeEx(nullptr,COINIT_MULTITHREADED);int result=0;
    try{
        std::wstring mode=argc>1?argv[1]:L"help";
        if(mode==L"inspect")Nodes(mode);
        else if(mode==L"install"&&argc==3)Install(argv[2]);
        else if(mode==L"remove")Nodes(mode);
        else if((mode==L"tone"||mode==L"capture")&&argc==3){Hr(initialized,"COM initialize");result=Audio(mode,argv[2]);}
        else if(mode==L"selftest"){
            std::vector<short> data(48000);for(size_t i=0;i<data.size();++i)data[i]=static_cast<short>(8000*std::sin(6.283185307179586*440*static_cast<double>(i)/48000));
            if(ToneScore(data,0,data.size())<.99)throw std::runtime_error("Tone detector selftest failed");
            std::fill(data.begin(),data.end(),static_cast<short>(0));if(ToneScore(data,0,data.size())!=0)throw std::runtime_error("Silence selftest failed");
            static_assert(sizeof(LINKMIC_INFO)==32);std::cout<<"{\"passed\":true,\"hardwareAccessed\":false}\n";
        }else{std::cout<<"LinkMicDriverTools inspect | install <signed.inf> | remove | tone <output.wav> | capture <output.wav> | selftest\n";result=mode==L"help"?0:1;}
    }catch(const std::exception& error){std::cerr<<error.what()<<"\n";result=1;}
    if(SUCCEEDED(initialized))CoUninitialize();return result;
}
