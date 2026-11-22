package com.voiceup.app.domain.repository

import com.voiceup.app.domain.model.AuthUser
import kotlinx.coroutines.flow.Flow

interface AuthRepository {
    /** null يعني لا يوجد مستخدم مسجل. */
    val authState: Flow<AuthUser?>

    /** ترمي AuthException عند الفشل. */
    suspend fun signIn(email: String, password: String)
    suspend fun signUp(email: String, password: String)
    fun signOut()
}
