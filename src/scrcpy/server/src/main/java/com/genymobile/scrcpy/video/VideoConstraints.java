package com.genymobile.scrcpy.video;

public class VideoConstraints {
    private final int maxSize;
    private final int alignment;
    private final VideoSizeCapabilities capabilities;

    public VideoConstraints(int maxSize, int alignment, VideoSizeCapabilities capabilities) {
        assert maxSize >= 0 : "Max size must not be negative";
        assert alignment > 0 : "Alignment must be positive";
        assert (alignment & (alignment - 1)) == 0 : "Alignment must be a power-of-two";
        this.maxSize = maxSize;
        this.alignment = alignment;
        this.capabilities = capabilities;
    }

    /** Return the requested maximum dimension, or zero when absent. */
    public int getMaxSize() {
        return maxSize;
    }

    public int getAlignment() {
        return alignment;
    }

    /** Declared encoder constraints remain absent until an actual failed attempt. */
    public VideoSizeCapabilities getEncoderCapabilities() {
        return capabilities;
    }

    public VideoConstraints withMaxSize(int value) {
        return new VideoConstraints(value, alignment, capabilities);
    }

    public VideoConstraints withCapabilities(VideoSizeCapabilities value) {
        return new VideoConstraints(maxSize, alignment, value);
    }
}
