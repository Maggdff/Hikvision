package com.voiceup.app.data

import com.google.firebase.FirebaseNetworkException
import com.google.firebase.auth.FirebaseAuth
import com.google.firebase.auth.FirebaseAuthInvalidCredentialsException
import com.google.firebase.auth.FirebaseAuthInvalidUserException
import com.google.firebase.auth.FirebaseAuthUserCollisionException
import com.google.firebase.auth.FirebaseAuthWeakPasswordException
import com.voiceup.app.domain.model.AuthError
import com.voiceup.app.domain.model.AuthException
import com.voiceup.app.domain.model.AuthUser
import com.voiceup.app.domain.repository.AuthRepository
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.tasks.await

class FirebaseAuthRepository(private val auth: FirebaseAuth = FirebaseAuth.getInstance()) : AuthRepository {

    override val authState: Flow<AuthUser?> = callbackFlow {
        val listener = FirebaseAuth.AuthStateListener { fa ->
            trySend(fa.currentUser?.let { AuthUser(it.uid, it.email) })
        }
        auth.addAuthStateListener(listener)
        awaitClose { auth.removeAuthStateListener(listener) }
    }

    override suspend fun signIn(email: String, password: String) {
        try {
            auth.signInWithEmailAndPassword(email.trim(), password).await()
        } catch (e: Exception) {
            throw map(e)
        }
    }

    override suspend fun signUp(email: String, password: String) {
        try {
            auth.createUserWithEmailAndPassword(email.trim(), password).await()
        } catch (e: Exception) {
            throw map(e)
        }
    }

    override fun signOut() = auth.signOut()

    private fun map(e: Exception): AuthException = AuthException(
        when (e) {
            is FirebaseAuthUserCollisionException -> AuthError.EmailInUse
            is FirebaseAuthWeakPasswordException -> AuthError.WeakPassword
            is FirebaseAuthInvalidUserException,
            is FirebaseAuthInvalidCredentialsException -> AuthError.InvalidCredentials
            is FirebaseNetworkException -> AuthError.Network
            else -> AuthError.Unknown
        }
    )
}
