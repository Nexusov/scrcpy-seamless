package com.genymobile.scrcpy.video;

import android.media.MediaCodecInfo;

/** Read the actual codec ranges and coupled size predicate without approximation. */
final class AndroidVideoSizeCapabilities implements VideoSizeCapabilities {
    private final MediaCodecInfo.VideoCapabilities capabilities;

    AndroidVideoSizeCapabilities(MediaCodecInfo.VideoCapabilities capabilities) {
        this.capabilities = capabilities;
    }

    @Override
    public int getWidthAlignment() {
        return capabilities.getWidthAlignment();
    }

    @Override
    public int getHeightAlignment() {
        return capabilities.getHeightAlignment();
    }

    @Override
    public int getMinWidth() {
        return capabilities.getSupportedWidths().getLower();
    }

    @Override
    public int getMaxWidth() {
        return capabilities.getSupportedWidths().getUpper();
    }

    @Override
    public int getMinHeight() {
        return capabilities.getSupportedHeights().getLower();
    }

    @Override
    public int getMaxHeight() {
        return capabilities.getSupportedHeights().getUpper();
    }

    @Override
    public int getMaxWidthForHeight(int height) {
        return capabilities.getSupportedWidthsFor(height).getUpper();
    }

    @Override
    public int getMaxHeightForWidth(int width) {
        return capabilities.getSupportedHeightsFor(width).getUpper();
    }

    @Override
    public boolean isSizeSupported(int width, int height) {
        return capabilities.isSizeSupported(width, height);
    }
}
