package com.example.p2paudio.transport

interface AudioTransport {
    val mode: TransportMode

    fun close()
}
