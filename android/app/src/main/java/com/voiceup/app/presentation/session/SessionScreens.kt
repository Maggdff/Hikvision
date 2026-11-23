package com.voiceup.app.presentation.session

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.presentation.theme.appBackground

@Composable
fun SessionLoadingScreen() {
    Box(
        Modifier.fillMaxSize().background(appBackground()),
        contentAlignment = Alignment.Center,
    ) { CircularProgressIndicator() }
}

@Composable
fun SessionErrorScreen(onRetry: () -> Unit, onSignOut: () -> Unit) {
    Box(
        Modifier.fillMaxSize().background(appBackground()),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(stringResource(R.string.session_error))
            Button(onClick = onRetry) { Text(stringResource(R.string.home_retry)) }
            TextButton(onClick = onSignOut) { Text(stringResource(R.string.sign_out)) }
        }
    }
}
