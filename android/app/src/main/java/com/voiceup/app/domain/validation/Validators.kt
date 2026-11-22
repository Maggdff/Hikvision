package com.voiceup.app.domain.validation

import java.time.LocalDate
import java.time.Period

object Validators {
    const val MIN_AGE = 18
    const val BIO_MAX = 160
    private val USERNAME = Regex("^[a-z0-9_]{3,20}$")
    private val EMAIL = Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")

    /** اسم المستخدم يُخزَّن بحروف صغيرة لضمان التفرد. */
    fun normalizeUsername(raw: String): String = raw.trim().lowercase()

    fun isValidUsername(raw: String): Boolean = USERNAME.matches(normalizeUsername(raw))

    fun isValidEmail(raw: String): Boolean = EMAIL.matches(raw.trim())

    fun isValidPassword(raw: String): Boolean = raw.length >= 8

    fun isValidDisplayName(raw: String): Boolean = raw.trim().length in 1..30

    fun isValidBio(raw: String): Boolean = raw.length <= BIO_MAX

    fun ageOn(birthDate: LocalDate, today: LocalDate): Int = Period.between(birthDate, today).years

    fun isAdult(birthDate: LocalDate, today: LocalDate): Boolean =
        !birthDate.isAfter(today) && ageOn(birthDate, today) >= MIN_AGE

    /** يحوّل إدخال يوم/شهر/سنة إلى تاريخ، أو null إن كان غير صالح. */
    fun parseBirthDate(day: String, month: String, year: String): LocalDate? = runCatching {
        val y = year.trim().toInt()
        if (y < 1900) return null
        LocalDate.of(y, month.trim().toInt(), day.trim().toInt())
    }.getOrNull()
}
