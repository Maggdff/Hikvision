package com.voiceup.app.presentation.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

private val avatarPalettes = listOf(
    Color(0xFF0FB58A) to Color(0xFF0E7490),
    Color(0xFFFF7A45) to Color(0xFFE5484D),
    Color(0xFF3B82F6) to Color(0xFF06B6D4),
    Color(0xFF8B5CF6) to Color(0xFF3B82F6),
    Color(0xFFF59E0B) to Color(0xFFFF7A45),
    Color(0xFFEC4899) to Color(0xFF8B5CF6),
)

/** صورة رمزية مولَّدة: حرف أول على تدرج ثابت لكل مستخدم (من seed). رفع الصور الحقيقية يأتي لاحقاً. */
@Composable
fun Avatar(name: String, seed: String, size: Dp = 48.dp, modifier: Modifier = Modifier) {
    val (a, b) = avatarPalettes[Math.floorMod(seed.hashCode(), avatarPalettes.size)]
    Box(
        modifier = modifier
            .size(size)
            .clip(CircleShape)
            .background(Brush.linearGradient(listOf(a, b))),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = name.trim().take(1).uppercase().ifEmpty { "؟" },
            color = Color.White,
            style = if (size >= 56.dp) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleMedium,
        )
    }
}
