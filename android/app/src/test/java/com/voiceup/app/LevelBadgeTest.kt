package com.voiceup.app

import com.voiceup.app.presentation.components.levelTier
import org.junit.Assert.assertEquals
import org.junit.Test

class LevelBadgeTest {
    @Test fun tiersFollowTheTenLevelRanges() {
        val expected = mapOf(
            1 to 1, 19 to 1, 20 to 2, 39 to 2, 40 to 3, 49 to 3, 50 to 4, 59 to 4, 60 to 5, 69 to 5,
            70 to 6, 79 to 6, 80 to 7, 89 to 7, 90 to 8, 99 to 8, 100 to 9, 125 to 9, 126 to 10, 150 to 10,
        )
        for ((level, tier) in expected) assertEquals("level $level", tier, levelTier(level))
    }
}
