using OblivionUI;
using OblivionVoice.Client.Audio;
using OblivionVoice.Common;
namespace OblivionVoice.Client;

internal static class VoiceSettingsBook
{
    private static BookHandle? book;
    private static VoiceRuntime? owner;
    private const string BindingId="OblivionVoice.speak";
    private static void RestoreBinding(VoiceRuntime runtime){KeyBindings.Release(BindingId);try{KeyBindings.Assign(BindingId,runtime.EffectiveTransmitKey);}catch(ArgumentException){}}
    internal static void Close()
    {
        book?.Dispose();book=null;if(owner is {} runtime){RestoreBinding(runtime);runtime.SetSettingsOpen(false);}owner=null;
    }
    internal static void Open(VoiceRuntime runtime)
    {
        if(book?.IsOpen==true){Close();return;}
        runtime.EnsurePreferencesLoaded();
        IReadOnlyList<WasapiMicrophoneCapture.InputDevice> devices;
        try {devices=WasapiMicrophoneCapture.ListInputDevices();}
        catch {Notifications.Show("Voice settings","Could not list microphones. Check Windows audio devices.");return;}
        var choices=devices.ToList();var draft=runtime.Preferences;
        int deviceIndex=choices.FindIndex(d=>d.Id==draft.InputDeviceId);
        if(deviceIndex<0){choices.Add(new(draft.InputDeviceId,"Saved microphone (currently unavailable)"));deviceIndex=choices.Count-1;}
        RestoreBinding(runtime);
        owner=runtime;runtime.SetSettingsOpen(true);
        BookHandle? opened=null;
        opened=Books.Open(new(){Title="Voice settings",ShowTabs=true,Subtitle=$"Speak key: {runtime.EffectiveTransmitKey}  |  Changes apply when saved",OnClose=()=>{
            if(ReferenceEquals(book,opened)){book=null;owner=null;RestoreBinding(runtime);runtime.SetSettingsOpen(false);}
        },Pages=[new(){Title="Microphone",Gap=12,Elements=[
            new(){Id="voice-device",Kind=BookControl.Dropdown,Tooltip="Uses the saved Windows microphone ID. A disconnected device remains listed until you choose another.",Label="Input device",Description="Choose your microphone, or follow the Windows default.",Options=choices.Select(d=>d.Name).ToArray(),SelectedIndex=deviceIndex,OnChange=e=>draft=draft with {InputDeviceId=choices[e.SelectedIndex].Id}},
            new(){Id="voice-gain",Kind=BookControl.Slider,Tooltip="0 dB leaves the original volume unchanged. High gain may clip loud speech.",Label="Microphone gain",Description="-24 dB left | 0 dB centre | +24 dB right. Centre keeps the original level.",Minimum=-24,Maximum=24,Step=1,Value=draft.InputGainDb,OnChange=e=>draft=draft with {InputGainDb=(float)e.Value}},
        ]},new(){Title="Speaking",Gap=12,Elements=[
            new(){Id=BindingId,Kind=BookControl.Keybind,Label="Speak key",Text=runtime.EffectiveTransmitKey,DefaultKey=runtime.Settings.TransmitKey,Tooltip="Activate then press a keyboard key. Escape cancels. Backspace restores the server default. Save on the Apply page.",OnChange=e=>draft=draft with {TransmitKey=e.Text}},
            new(){Id="voice-mode",Kind=BookControl.Dropdown,Label="Transmit mode",Description="Hold the speak key, or press once to start and again to stop.",Options=["Use server default","Push to talk (hold)","Toggle to talk"],SelectedIndex=draft.TransmitMode is null ? 0 : draft.TransmitMode==TransmitMode.Hold ? 1 : 2,OnChange=e=>draft=draft with {TransmitMode=e.SelectedIndex==0 ? null : e.SelectedIndex==1 ? TransmitMode.Hold : TransmitMode.Toggle}},
            new(){Id="voice-mouth-enabled",Kind=BookControl.Toggle,Label="Talking mouth animation",Description="Animate your character and audible players as they speak.",Checked=draft.MouthAnimationEnabled,OnChange=e=>draft=draft with {MouthAnimationEnabled=e.Checked}},
            new(){Id="voice-mouth-strength",Kind=BookControl.Slider,Label="Mouth movement",Description="Affects how widely the mouth opens while speaking.",Minimum=0,Maximum=100,Step=5,Value=draft.MouthAnimationStrength*100,Height=150,OnChange=e=>draft=draft with {MouthAnimationStrength=(float)e.Value/100}},
            new(){Id="voice-help-section",Kind=BookControl.Section,Label="Voice controls",Checked=false,Height=48},
            new(){Id="voice-help",Kind=BookControl.Text,SectionId="voice-help-section",Label="Existing shortcuts",Description=$"{runtime.Settings.CycleRangeKey}: voice range. {runtime.Settings.ToggleMuteKey}: mute. F5: settings. Changes apply only when saved.",Height=130}
        ]},new(){Title="Apply",Gap=12,Elements=[
            new(){Id="voice-status",Kind=BookControl.Text,Label=runtime.IsVoiceConnected?"Connected":"Not connected",Description="Connection status when this menu opened. Speaking stays off while settings are open.",Height=110},
            new(){Id="voice-apply",Kind=BookControl.Button,Label="Save your preferences",Description="Speaking stays off while this book is open. Close discards unsaved changes.",ButtonLabel="Apply and save",OnChange=e=>{
                try {runtime.ApplyPreferences(draft);e.Book.Update("voice-apply",x=>x with {Description=$"Saved. Microphone gain: {draft.InputGainDb:+0;-0;0} dB. Close when ready to speak."});Notifications.Show("Voice settings","Microphone preferences saved.");}
                catch(Exception ex){e.Book.Update("voice-apply",x=>x with {Description="Could not apply. Check the microphone and try again."});Notifications.Show("Voice settings",ex.Message);}
            }}
        ]}]});
        book=opened;
    }
}
