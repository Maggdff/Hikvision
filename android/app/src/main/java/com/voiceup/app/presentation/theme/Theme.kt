package com.voiceup.app.presentation.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color

object VoiceUpColors {
    val Background = Color(0xFF0B0B14)
    val Surface = Color(0xFF15152A)
    val SurfaceHigh = Color(0xFF1F1F3A)
    val Violet = Color(0xFF8B5CF6)
    val Blue = Color(0xFF3B82F6)
    val Pink = Color(0xFFEC4899)
    val OnDark = Color(0xFFF1F0FF)
    val OnDarkMuted = Color(0xFFA9A8C9)

    val AccentGradient = Brush.linearGradient(listOf(Violet, Blue))
    val BackgroundGradient = Brush.verticalGradient(listOf(Color(0xFF1A1033), Background))
}

private val DarkScheme = darkColorScheme(
    primary = VoiceUpColors.Violet,
    secondary = VoiceUpColors.Blue,
    tertiary = VoiceUpColors.Pink,
    background = VoiceUpColors.Background,
    surface = VoiceUpColors.Surface,
    surfaceVariant = VoiceUpColors.SurfaceHigh,
    onBackground = VoiceUpColors.OnDark,
    onSurface = VoiceUpColors.OnDark,
    onSurfaceVariant = VoiceUpColors.OnDarkMuted,
)

/** الثيم داكن دائماً؛ اتجاه RTL يُحدَّد تلقائياً من لغة الجهاز (supportsRtl). */
@Composable
fun VoiceUpTheme(content: @Composable () -> Unit) {
    @Suppress("UNUSED_VARIABLE") val unused = isSystemInDarkTheme()
    MaterialTheme(colorScheme = DarkScheme, content = content)
}
