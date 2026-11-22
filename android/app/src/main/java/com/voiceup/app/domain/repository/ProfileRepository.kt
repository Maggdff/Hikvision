package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.ProfileDraft
import com.voiceup.app.domain.model.UserProfile

interface ProfileRepository {
    /** null إن لم يُنشأ الملف بعد. */
    suspend fun getProfile(uid: String): UserProfile?

    /** ترمي UsernameTakenException إن كان الاسم محجوزاً. */
    suspend fun createProfile(uid: String, draft: ProfileDraft)

    /** يحدّث الحقول القابلة للتعديل فقط (الاسم المعروض، النبذة، الدولة). اسم المستخدم وتاريخ الميلاد ثابتان. */
    suspend fun updateProfile(uid: String, displayName: String, bio: String, country: String?)
}
