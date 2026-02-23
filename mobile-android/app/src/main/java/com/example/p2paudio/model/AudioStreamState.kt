package com.example.p2paudio.model

enum class AudioStreamState {
    IDLE,
    CAPTURING,
    CONNECTING,
    STREAMING,
    INTERRUPTED,
    FAILED,
    ENDED
}
