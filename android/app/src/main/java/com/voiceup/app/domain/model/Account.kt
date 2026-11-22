package com.voiceup.app.domain.model

import java.time.LocalDate

data class AuthUser(val uid: String, val email: String?)

/** الملف العام فقط. تاريخ الميلاد لا يظهر هنا ويُحفظ في وثيقة خاصة بالمالك. */
data class UserProfile(
    val uid: String,
    val username: String,
    val displayName: String,
    val bio: String,
)

data class ProfileDraft(
    val username: String,
    val displayName: String,
    val bio: String,
    val birthDate: LocalDate,
)

enum class AuthError { InvalidCredentials, EmailInUse, WeakPassword, InvalidEmail, Network, Unknown }

class AuthException(val error: AuthError) : Exception(error.name)

class UsernameTakenException : Exception("username taken")
