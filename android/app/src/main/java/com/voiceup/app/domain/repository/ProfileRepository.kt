package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UserProfile

interface ProfileRepository {
    /** null إن لم يُنشأ الملف بعد. */
    suspend fun getProfile(uid: String): UserProfile?

    /** ترمي UsernameTakenException إن كان الاسم محجوزاً. */
    suspend fun createProfile(uid: String, draft: ProfileDraft)
}
