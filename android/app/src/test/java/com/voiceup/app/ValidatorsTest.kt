package com.voiceup.app

import com.voiceup.app.domain.validation.Validators
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class ValidatorsTest {

    private val today = LocalDate.of(2026, 11, 21)

    @Test fun exactly18TodayIsAdult() =
        assertTrue(Validators.isAdult(LocalDate.of(2008, 11, 21), today))

    @Test fun turns18TomorrowIsNotAdult() =
        assertFalse(Validators.isAdult(LocalDate.of(2008, 11, 22), today))

    @Test fun futureBirthDateIsNotAdult() =
        assertFalse(Validators.isAdult(LocalDate.of(2027, 1, 1), today))

    @Test fun leapDayBirthdayCountsCorrectly() {
        // مولود 29 فبراير 2008: يبلغ 18 في 28 فبراير 2026 وفق Period
        assertTrue(Validators.isAdult(LocalDate.of(2008, 2, 29), LocalDate.of(2026, 3, 1)))
        assertFalse(Validators.isAdult(LocalDate.of(2008, 2, 29), LocalDate.of(2026, 2, 27)))
    }

    @Test fun parseBirthDateValid() =
        assertEquals(LocalDate.of(1995, 5, 7), Validators.parseBirthDate("7", "5", "1995"))

    @Test fun parseBirthDateRejectsInvalid() {
        assertNull(Validators.parseBirthDate("31", "2", "1995"))
        assertNull(Validators.parseBirthDate("", "5", "1995"))
        assertNull(Validators.parseBirthDate("1", "13", "1995"))
        assertNull(Validators.parseBirthDate("1", "1", "1800"))
    }

    @Test fun usernameRules() {
        assertTrue(Validators.isValidUsername("Ali_90"))
        assertFalse(Validators.isValidUsername("ab"))
        assertFalse(Validators.isValidUsername("a".repeat(21)))
        assertFalse(Validators.isValidUsername("علي"))
        assertFalse(Validators.isValidUsername("a b c"))
        assertEquals("ali_90", Validators.normalizeUsername("  Ali_90 "))
    }

    @Test fun emailAndPassword() {
        assertTrue(Validators.isValidEmail("a@b.co"))
        assertFalse(Validators.isValidEmail("a@b"))
        assertTrue(Validators.isValidPassword("12345678"))
        assertFalse(Validators.isValidPassword("1234567"))
    }
}
