package com.voiceup.app.domain.model

data class Country(val code: String, val nameAr: String, val nameEn: String)

/** الدول العربية المدعومة. يجب أن تطابق قائمة الخادم backend/src/rooms/countries.ts. */
object Countries {
    val all: List<Country> = listOf(
        Country("DZ", "الجزائر", "Algeria"),
        Country("BH", "البحرين", "Bahrain"),
        Country("KM", "جزر القمر", "Comoros"),
        Country("DJ", "جيبوتي", "Djibouti"),
        Country("EG", "مصر", "Egypt"),
        Country("IQ", "العراق", "Iraq"),
        Country("JO", "الأردن", "Jordan"),
        Country("KW", "الكويت", "Kuwait"),
        Country("LB", "لبنان", "Lebanon"),
        Country("LY", "ليبيا", "Libya"),
        Country("MR", "موريتانيا", "Mauritania"),
        Country("MA", "المغرب", "Morocco"),
        Country("OM", "عُمان", "Oman"),
        Country("PS", "فلسطين", "Palestine"),
        Country("QA", "قطر", "Qatar"),
        Country("SA", "السعودية", "Saudi Arabia"),
        Country("SO", "الصومال", "Somalia"),
        Country("SD", "السودان", "Sudan"),
        Country("SY", "سوريا", "Syria"),
        Country("TN", "تونس", "Tunisia"),
        Country("AE", "الإمارات", "United Arab Emirates"),
        Country("YE", "اليمن", "Yemen"),
    )

    private val byCode = all.associateBy { it.code }

    fun find(code: String?): Country? = code?.let { byCode[it] }
    fun isSupported(code: String): Boolean = byCode.containsKey(code)
}
