package com.voiceup.app.presentation.main

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.voiceup.app.R

enum class MainTab { Home, Account }

/**
 * التنقل السفلي. يحوي التبويبات العاملة فقط؛ تُضاف تبويبات (استكشاف، رسائل) عند بنائها فعلاً.
 */
@Composable
fun MainScaffold(
    home: @Composable () -> Unit,
    account: @Composable () -> Unit,
) {
    var tab by rememberSaveable { mutableStateOf(MainTab.Home) }
    Scaffold(
        bottomBar = {
            NavigationBar {
                tabItem(MainTab.Home, tab, R.string.nav_home) { tab = it }
                tabItem(MainTab.Account, tab, R.string.nav_account) { tab = it }
            }
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when (tab) {
                MainTab.Home -> home()
                MainTab.Account -> account()
            }
        }
    }
}

@Composable
private fun androidx.compose.foundation.layout.RowScope.tabItem(
    me: MainTab,
    current: MainTab,
    label: Int,
    onSelect: (MainTab) -> Unit,
) {
    val selected = me == current
    NavigationBarItem(
        selected = selected,
        onClick = { onSelect(me) },
        icon = {
            Box(
                Modifier.size(10.dp).background(
                    if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    CircleShape,
                ),
            )
        },
        label = { Text(stringResource(label)) },
        alwaysShowLabel = true,
    )
}
