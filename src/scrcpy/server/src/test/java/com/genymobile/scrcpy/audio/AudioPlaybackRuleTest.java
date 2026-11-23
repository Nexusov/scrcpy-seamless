package com.genymobile.scrcpy.audio;

import android.media.AudioAttributes;
import org.junit.Assert;
import org.junit.Test;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;

public class AudioPlaybackRuleTest {
    public static final class Rule {
        private final boolean voiceAllowed;
        private final List<Integer> usages;

        Rule(boolean voiceAllowed, List<Integer> usages) {
            this.voiceAllowed = voiceAllowed;
            this.usages = new ArrayList<>(usages);
        }
    }

    /** Snapshot on build makes a late mutation observable without Android services. */
    public static final class Builder {
        private final List<Integer> usages = new ArrayList<>();
        private boolean voiceAllowed;
        private int role;

        public void setTargetMixRole(int value) {
            role = value;
        }

        public void addMixRule(int matchRule, Object attributes) {
            Assert.assertEquals(7, matchRule);
            usages.add((Integer) attributes);
        }

        public void voiceCommunicationCaptureAllowed(boolean allowed) {
            voiceAllowed = allowed;
        }

        public Rule build() {
            return new Rule(voiceAllowed, usages);
        }
    }

    @Test
    public void appliesVoicePermissionBeforeImmutableBuild() throws ReflectiveOperationException {
        Builder builder = new Builder();
        Rule result = (Rule) AudioPlaybackRule.build(builder, 3, 7, usage -> usage);
        Assert.assertEquals(3, builder.role);
        Assert.assertTrue(result.voiceAllowed);
    }

    @Test
    public void includesGameAndAllReviewedUsagesExactlyOnce() throws ReflectiveOperationException {
        Rule result = (Rule) AudioPlaybackRule.build(new Builder(), 3, 7, usage -> usage);
        Assert.assertTrue(result.usages.contains(AudioAttributes.USAGE_GAME));
        Assert.assertTrue(result.usages.contains(AudioAttributes.USAGE_MEDIA));
        Assert.assertTrue(result.usages.contains(AudioAttributes.USAGE_VOICE_COMMUNICATION));
        Assert.assertEquals(13, result.usages.size());
        Assert.assertEquals(13, new HashSet<>(result.usages).size());
    }

    @Test(expected = NoSuchMethodException.class)
    public void unavailableFrameworkMethodRemainsAnExplicitFailure() throws ReflectiveOperationException {
        AudioPlaybackRule.build(new Object(), 3, 7, usage -> usage);
    }
}
