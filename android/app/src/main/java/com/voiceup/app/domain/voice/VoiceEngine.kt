package com.voiceup.app.domain.voice

import kotlinx.coroutines.flow.StateFlow

enum class VoiceState { Disconnected, Connecting, Connected, Reconnecting, Failed }

/** واجهة محرك الصوت؛ تنفيذها الحقيقي LiveKit، وتنفيذ بديل يمكن استخدامه في الاختبارات. */
interface VoiceEngine {
    val state: StateFlow<VoiceState>

    /** معرّفات (uid) من يتحدثون الآن. */
    val speaking: StateFlow<Set<String>>

    /** يرمي استثناءً عند الفشل. يستبدل أي اتصال سابق. */
    suspend fun connect(url: String, token: String)

    /** يرمي استثناءً إن لم تكن هناك صلاحية نشر أو لا اتصال. */
    suspend fun setMicEnabled(enabled: Boolean)

    fun disconnect()
}
