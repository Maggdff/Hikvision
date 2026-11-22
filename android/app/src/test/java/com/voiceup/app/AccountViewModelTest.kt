package com.voiceup.app

import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.presentation.account.AccountViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class AccountViewModelTest {

    @Before fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())
    @After fun tearDown() = Dispatchers.resetMain()

    private val profiles = FakeProfiles()
    private val me = UserProfile("u1", "sara", "سارة", "مرحبا", "SA")
    private fun vm() = AccountViewModel(me, profiles)

    @Test fun startEditCopiesCurrentValues() {
        val v = vm()
        v.startEdit()
        val s = v.state.value
        assertTrue(s.editing)
        assertEquals("سارة", s.displayName)
        assertEquals("SA", s.country)
    }

    @Test fun saveSendsTrimmedValuesAndUpdatesProfile() {
        val v = vm()
        v.startEdit()
        v.onDisplayName("  نور  ")
        v.onBio(" أحب القراءة ")
        v.onCountry("EG")
        v.save()
        assertEquals(listOf("u1:نور:أحب القراءة:EG"), profiles.updates)
        val s = v.state.value
        assertFalse(s.editing)
        assertEquals("نور", s.profile.displayName)
        assertEquals("أحب القراءة", s.profile.bio)
        assertEquals("EG", s.profile.country)
    }

    @Test fun blankNameIsRejectedWithoutCallingRepo() {
        val v = vm()
        v.startEdit()
        v.onDisplayName("   ")
        v.save()
        assertEquals("invalid_name", v.state.value.errorCode)
        assertTrue(profiles.updates.isEmpty())
        assertTrue(v.state.value.editing)
    }

    @Test fun failedSaveKeepsEditingAndShowsError() {
        profiles.failUpdate = true
        val v = vm()
        v.startEdit()
        v.onDisplayName("نور")
        v.save()
        assertEquals("save_failed", v.state.value.errorCode)
        assertTrue(v.state.value.editing)
        assertFalse(v.state.value.saving)
        assertEquals("سارة", v.state.value.profile.displayName)
    }

    @Test fun cancelDiscardsChanges() {
        val v = vm()
        v.startEdit()
        v.onDisplayName("غير محفوظ")
        v.cancelEdit()
        assertFalse(v.state.value.editing)
        assertEquals("سارة", v.state.value.profile.displayName)
        assertTrue(profiles.updates.isEmpty())
    }

    @Test fun unsupportedCountryIsDroppedOnSave() {
        val v = vm()
        v.startEdit()
        v.onCountry("US")
        v.save()
        assertNull(v.state.value.profile.country)
    }

    @Test fun inputIsCappedAtLimits() {
        val v = vm()
        v.startEdit()
        v.onDisplayName("ا".repeat(50))
        v.onBio("ب".repeat(300))
        assertEquals(30, v.state.value.displayName.length)
        assertEquals(160, v.state.value.bio.length)
    }
}
