package com.voiceup.app.presentation

import androidx.annotation.StringRes
import com.voiceup.app.R

/** يحوّل رمز خطأ الخادم/الشبكة إلى رسالة مفهومة للمستخدم. */
@StringRes
fun errorMessageRes(code: String): Int = when (code) {
    "network" -> R.string.err_network
    "not_configured" -> R.string.err_not_configured
    "unauthenticated" -> R.string.err_unauthenticated
    "active_room_exists" -> R.string.err_active_room_exists
    "banned_from_room" -> R.string.err_banned
    "room_not_found" -> R.string.err_room_closed
    "owner_only" -> R.string.err_owner_only
    "staff_only", "insufficient_role" -> R.string.err_no_permission
    "no_free_seat" -> R.string.err_no_free_seat
    "already_seated" -> R.string.err_already_seated
    "not_seated" -> R.string.err_not_seated
    "owner_cannot_leave_seat" -> R.string.err_owner_cannot_leave_seat
    "invalid_title" -> R.string.err_invalid_title
    "invalid_topic" -> R.string.err_invalid_topic
    "wrong_password" -> R.string.err_wrong_password
    "too_many_attempts" -> R.string.err_too_many_attempts
    "rate_limited" -> R.string.err_rate_limited
    "invalid_message" -> R.string.err_invalid_message
    "join_required", "password_required" -> R.string.err_join_required
    "invalid_password" -> R.string.err_invalid_password
    "invalid_country" -> R.string.err_invalid_country
    "voice_failed" -> R.string.err_voice_failed
    "mic_failed" -> R.string.err_mic_failed
    else -> R.string.err_unknown
}

/** اسم الدولة حسب لغة الجهاز (عربي أو إنجليزي). */
fun countryName(c: com.voiceup.app.domain.model.Country): String =
    if (java.util.Locale.getDefault().language == "ar") c.nameAr else c.nameEn
