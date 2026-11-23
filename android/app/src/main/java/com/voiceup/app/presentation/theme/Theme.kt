package com.voiceup.app.presentation.theme

import android.app.Activity
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

/** هوية VoiceUp: أخضر زمردي مع سماوي وبرتقالي دافئ كلون إبراز. التطبيق فاتح، وشاشة الغرفة داكنة. */
object VoiceUpColors {
    val Emerald = Color(0xFF0FB58A)
    val Cyan = Color(0xFF06B6D4)
    val Orange = Color(0xFFFF7A45)
    val Violet = Color(0xFF8B5CF6)

    // للاستخدام فوق خلفية الغرفة الداكنة
    val Mint = Color(0xFF5EEAD4)
    val Coral = Color(0xFFFF8A65)

    val AccentGradient = Brush.linearGradient(listOf(Emerald, Cyan))
}

private val LightScheme = lightColorScheme(
    primary = VoiceUpColors.Emerald,
    onPrimary = Color.White,
    secondary = VoiceUpColors.Cyan,
    tertiary = VoiceUpColors.Orange,
    background = Color(0xFFF4F8F7),
    surface = Color.White,
    surfaceVariant = Color(0xFFE8F0EE),
    onBackground = Color(0xFF14201D),
    onSurface = Color(0xFF14201D),
    onSurfaceVariant = Color(0xFF5E6F6B),
    error = Color(0xFFD93025),
)

private val DarkScheme = darkColorScheme(
    primary = VoiceUpColors.Mint,
    onPrimary = Color(0xFF00201B),
    secondary = VoiceUpColors.Cyan,
    tertiary = VoiceUpColors.Coral,
    background = Color(0xFF0B1A1A),
    surface = Color(0xFF14282A),
    surfaceVariant = Color(0xFF1E3A3C),
    onBackground = Color(0xFFEAF7F4),
    onSurface = Color(0xFFEAF7F4),
    onSurfaceVariant = Color(0xFFA3BDB8),
)

private val LocalDark = compositionLocalOf { false }

/** خلفية الشاشة حسب الثيم الحالي. */
@Composable
fun appBackground(): Brush =
    if (LocalDark.current) Brush.verticalGradient(listOf(Color(0xFF123B3A), Color(0xFF0B1A1A)))
    else Brush.verticalGradient(listOf(Color(0xFFDDF5EE), Color(0xFFF4F8F7)))

/** dark=true للغرفة فقط. اتجاه RTL يُحدَّد تلقائياً من لغة الجهاز. */
@Composable
fun VoiceUpTheme(dark: Boolean = false, content: @Composable () -> Unit) {
    val view = LocalView.current
    if (!view.isInEditMode) {
        // أيقونات شريط الحالة: داكنة على الخلفية الفاتحة وفاتحة على الداكنة؛ تُستعاد عند مغادرة الشاشة
        DisposableEffect(dark) {
            val window = (view.context as? Activity)?.window
            fun apply(light: Boolean) {
                window?.let {
                    val c = WindowCompat.getInsetsController(it, view)
                    c.isAppearanceLightStatusBars = light
                    c.isAppearanceLightNavigationBars = light
                }
            }
            apply(!dark)
            onDispose { apply(true) }
        }
    }
    CompositionLocalProvider(LocalDark provides dark) {
        MaterialTheme(colorScheme = if (dark) DarkScheme else LightScheme, content = content)
    }
}
