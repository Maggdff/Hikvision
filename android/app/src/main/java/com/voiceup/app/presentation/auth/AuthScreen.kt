package com.voiceup.app.presentation.auth

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.voiceup.app.R
import com.voiceup.app.domain.model.AuthError
import com.voiceup.app.presentation.theme.appBackground

@Composable
fun AuthScreen(
    state: AuthUiState,
    onEmail: (String) -> Unit,
    onPassword: (String) -> Unit,
    onSubmit: () -> Unit,
    onToggleMode: () -> Unit,
) {
    Column(
        Modifier
            .fillMaxSize()
            .background(appBackground())
            .safeDrawingPadding()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp, androidx.compose.ui.Alignment.CenterVertically),
    ) {
        Text(
            stringResource(R.string.app_name),
            style = MaterialTheme.typography.displaySmall,
            color = MaterialTheme.colorScheme.primary,
        )
        Text(
            stringResource(if (state.isSignUp) R.string.auth_title_signup else R.string.auth_title_signin),
            style = MaterialTheme.typography.titleLarge,
        )
        OutlinedTextField(
            value = state.email,
            onValueChange = onEmail,
            label = { Text(stringResource(R.string.auth_email)) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedTextField(
            value = state.password,
            onValueChange = onPassword,
            label = { Text(stringResource(R.string.auth_password)) },
            supportingText = if (state.isSignUp) {
                { Text(stringResource(R.string.auth_password_hint)) }
            } else null,
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            modifier = Modifier.fillMaxWidth(),
        )
        state.error?.let {
            Text(stringResource(errorRes(it)), color = MaterialTheme.colorScheme.error)
            state.errorDetail?.let { d ->
                Text(
                    stringResource(R.string.auth_error_detail, d),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        Button(onClick = onSubmit, enabled = !state.loading, modifier = Modifier.fillMaxWidth()) {
            if (state.loading) {
                CircularProgressIndicator(Modifier.padding(2.dp), strokeWidth = 2.dp)
            } else {
                Text(stringResource(if (state.isSignUp) R.string.auth_action_signup else R.string.auth_action_signin))
            }
        }
        TextButton(onClick = onToggleMode, enabled = !state.loading) {
            Text(stringResource(if (state.isSignUp) R.string.auth_switch_to_signin else R.string.auth_switch_to_signup))
        }
    }
}

private fun errorRes(e: AuthError): Int = when (e) {
    AuthError.InvalidCredentials -> R.string.auth_error_invalid_credentials
    AuthError.EmailInUse -> R.string.auth_error_email_in_use
    AuthError.WeakPassword -> R.string.auth_error_weak_password
    AuthError.InvalidEmail -> R.string.auth_error_invalid_email
    AuthError.Network -> R.string.auth_error_network
    AuthError.NotEnabled -> R.string.auth_error_not_enabled
    AuthError.Unknown -> R.string.auth_error_unknown
}
