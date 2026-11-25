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

/**
 * هوية VoiceUp (من تصميم «مجلس»): أسود مخضرّ عميق مع عنبري دافئ كلون إبراز.
 * التطبيق داكن افتراضياً، وشاشة «حسابي» على خلفية كريمية فاتحة.
 */
object VoiceUpColors {
    val Amber = Color(0xFFF4A93B)
    val OnAmber = Color(0xFF2A1A00)
    val Ink = Color(0xFF0F1512)
    val Surface = Color(0xFF17201B)
    val Surface2 = Color(0xFF212C26)
    val Cream = Color(0xFFF4EFE6)
    val Muted = Color(0xFF8FA095)
    val Line = Color(0x17F4EFE6)

    // أسماء قديمة يستعملها كود الغرفة: كلها الآن العنبري
    val Mint = Amber
    val Coral = Amber
    val Orange = Amber
    val Emerald = Amber
    val Cyan = Amber
    val Violet = Amber

    val AccentGradient = Brush.linearGradient(listOf(Amber, Amber))
}

private val LightScheme = lightColorScheme(
    primary = VoiceUpColors.Amber,
    onPrimary = VoiceUpColors.OnAmber,
    secondary = Color(0xFF0F1512),
    tertiary = VoiceUpColors.Amber,
    background = Color(0xFFF6F1E7),
    surface = Color.White,
    surfaceVariant = Color(0xFFEDE6D8),
    onBackground = VoiceUpColors.Ink,
    onSurface = VoiceUpColors.Ink,
    onSurfaceVariant = Color(0xFF5C675F),
    outline = Color(0xFFD8D0C0),
    error = Color(0xFFD93025),
)

private val DarkScheme = darkColorScheme(
    primary = VoiceUpColors.Amber,
    onPrimary = VoiceUpColors.OnAmber,
    secondary = VoiceUpColors.Surface2,
    tertiary = VoiceUpColors.Amber,
    background = VoiceUpColors.Ink,
    surface = VoiceUpColors.Surface,
    surfaceVariant = VoiceUpColors.Surface2,
    onBackground = VoiceUpColors.Cream,
    onSurface = VoiceUpColors.Cream,
    onSurfaceVariant = VoiceUpColors.Muted,
    outline = VoiceUpColors.Line,
    error = Color(0xFFFF8A80),
)

private val LocalDark = compositionLocalOf { false }

/** خلفية الشاشة حسب الثيم الحالي. */
@Composable
fun appBackground(): Brush =
    if (LocalDark.current) Brush.verticalGradient(listOf(VoiceUpColors.Ink, VoiceUpColors.Ink))
    else Brush.verticalGradient(listOf(Color(0xFFF6F1E7), Color(0xFFF6F1E7)))

/** داكن افتراضياً؛ dark=false لشاشة «حسابي». اتجاه RTL يُحدَّد تلقائياً من لغة الجهاز. */
@Composable
fun VoiceUpTheme(dark: Boolean = true, content: @Composable () -> Unit) {
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
            onDispose { apply(false) }
        }
    }
    CompositionLocalProvider(LocalDark provides dark) {
        MaterialTheme(colorScheme = if (dark) DarkScheme else LightScheme, content = content)
    }
}
