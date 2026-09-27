// SeaSick man-overboard haptics (phase 5a).
//
// Deliberately NOT Handheld.Vibrate: that call deadlocked iOS 26's audio
// daemon at launch (audiomxd XPC timeout, phone froze) and is banned
// project-wide (see Ship/JuiceTuning.cs). UIImpactFeedbackGenerator and
// UINotificationFeedbackGenerator are a separate Core Haptics-backed path
// that never touches the audio session, which is what made that call
// dangerous in the first place.
//
// Every function must run on the main thread -- Unity's managed->native
// calls from gameplay code already are, but dispatch_async is cheap
// insurance against a future caller off the main thread.

#import <UIKit/UIKit.h>

extern "C" {

void SeaSick_HapticImpactHeavy()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIImpactFeedbackGenerator *gen =
            [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        [gen prepare];
        [gen impactOccurred];
    });
}

void SeaSick_HapticNotifyWarning()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
        [gen prepare];
        [gen notificationOccurred:UINotificationFeedbackTypeWarning];
    });
}

void SeaSick_HapticNotifyError()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UINotificationFeedbackGenerator *gen = [[UINotificationFeedbackGenerator alloc] init];
        [gen prepare];
        [gen notificationOccurred:UINotificationFeedbackTypeError];
    });
}

}
