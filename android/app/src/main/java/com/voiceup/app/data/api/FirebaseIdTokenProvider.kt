package com.voiceup.app.data.api

import com.google.firebase.auth.FirebaseAuth
import kotlinx.coroutines.tasks.await

class FirebaseIdTokenProvider(private val auth: FirebaseAuth = FirebaseAuth.getInstance()) {
    suspend fun token(): String {
        val user = auth.currentUser ?: error("not signed in")
        // false: يستخدم الرمز المخزن ويجدده تلقائياً قبل انتهائه
        return user.getIdToken(false).await().token ?: error("no token")
    }
}
