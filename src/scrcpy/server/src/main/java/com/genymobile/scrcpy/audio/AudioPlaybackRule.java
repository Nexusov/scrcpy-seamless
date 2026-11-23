package com.genymobile.scrcpy.audio;

import com.genymobile.scrcpy.AndroidVersions;

import android.annotation.TargetApi;
import android.media.AudioAttributes;

import java.lang.reflect.Method;
import java.util.function.IntFunction;

/** Reflection boundary shared by playback capture and isolated builder tests. */
final class AudioPlaybackRule {
    private AudioPlaybackRule() {
    }

    @TargetApi(AndroidVersions.API_26_ANDROID_8_0)
    static Object build(Object builder, int role, int matchRule, IntFunction<Object> attributes) throws ReflectiveOperationException {
        Class<?> builderClass = builder.getClass();
        builderClass.getMethod("setTargetMixRole", int.class).invoke(builder, role);
        Method addMixRule = builderClass.getMethod("addMixRule", int.class, Object.class);
        int[] usages = {
            AudioAttributes.USAGE_ALARM,
            AudioAttributes.USAGE_ASSISTANCE_ACCESSIBILITY,
            AudioAttributes.USAGE_ASSISTANCE_NAVIGATION_GUIDANCE,
            AudioAttributes.USAGE_ASSISTANCE_SONIFICATION,
            AudioAttributes.USAGE_ASSISTANT,
            AudioAttributes.USAGE_GAME,
            AudioAttributes.USAGE_MEDIA,
            AudioAttributes.USAGE_NOTIFICATION,
            AudioAttributes.USAGE_NOTIFICATION_EVENT,
            AudioAttributes.USAGE_NOTIFICATION_RINGTONE,
            AudioAttributes.USAGE_UNKNOWN,
            AudioAttributes.USAGE_VOICE_COMMUNICATION,
            AudioAttributes.USAGE_VOICE_COMMUNICATION_SIGNALLING,
        };

        for (int usage : usages) {
            addMixRule.invoke(builder, matchRule, attributes.apply(usage));
        }
        builderClass.getMethod("voiceCommunicationCaptureAllowed", boolean.class).invoke(builder, true);
        return builderClass.getMethod("build").invoke(builder);
    }
}
