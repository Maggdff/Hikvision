package com.voiceup.app.domain.model

import java.time.LocalDate

data class AuthUser(val uid: String, val email: String?)

/** الملف العام فقط. تاريخ الميلاد لا يظهر هنا ويُحفظ في وثيقة خاصة بالمالك. */
data class UserProfile(
    val uid: String,
    val username: String,
    val displayName: String,
    val bio: String,
    /** رمز ISO لدولة المستخدم (اختياري). */
    val country: String? = null,
)

data class ProfileDraft(
    val username: String,
    val displayName: String,
    val bio: String,
    val birthDate: LocalDate,
)

enum class AuthError { InvalidCredentials, EmailInUse, WeakPassword, InvalidEmail, Network, NotEnabled, Unknown }

/** detail: رمز/وصف تقني للمطوّر (مثل ERROR_OPERATION_NOT_ALLOWED)، لا يحوي بيانات المستخدم. */
class AuthException(val error: AuthError, val detail: String? = null) : Exception(error.name)

class UsernameTakenException : Exception("username taken")
