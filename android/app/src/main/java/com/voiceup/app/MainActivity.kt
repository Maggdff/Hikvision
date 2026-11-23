package com.voiceup.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.voiceup.app.data.FirebaseAuthRepository
import com.voiceup.app.data.FirestoreProfileRepository
import com.voiceup.app.data.api.FirebaseIdTokenProvider
import com.voiceup.app.data.api.HttpRoomsRepository
import com.voiceup.app.data.voice.LiveKitVoiceEngine
import com.voiceup.app.domain.model.UserProfile
import com.voiceup.app.presentation.auth.AuthScreen
import com.voiceup.app.presentation.account.AccountScreen
import com.voiceup.app.presentation.account.AccountViewModel
import com.voiceup.app.presentation.auth.AuthViewModel
import com.voiceup.app.presentation.main.MainScaffold
import com.voiceup.app.presentation.home.HomeScreen
import com.voiceup.app.presentation.home.HomeViewModel
import com.voiceup.app.presentation.profile.ProfileSetupScreen
import com.voiceup.app.presentation.profile.ProfileSetupViewModel
import com.voiceup.app.presentation.room.RoomScreen
import com.voiceup.app.presentation.room.RoomViewModel
import com.voiceup.app.presentation.session.SessionErrorScreen
import com.voiceup.app.presentation.session.SessionLoadingScreen
import com.voiceup.app.presentation.session.SessionState
import com.voiceup.app.presentation.session.SessionViewModel
import com.voiceup.app.presentation.theme.VoiceUpTheme

/** مصنع بسيط للـ ViewModels (حقن يدوي مؤقت قبل اعتماد حاوية تبعيات). */
private fun <T : ViewModel> factory(create: () -> T) = object : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <V : ViewModel> create(modelClass: Class<V>): V = create() as V
}

class MainActivity : ComponentActivity() {

    private val authRepo by lazy { FirebaseAuthRepository() }
    private val profileRepo by lazy { FirestoreProfileRepository() }
    private val roomsRepo by lazy {
        val tokens = FirebaseIdTokenProvider()
        HttpRoomsRepository(BuildConfig.API_BASE_URL) { tokens.token() }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent { VoiceUpTheme { Root() } }
    }

    @Composable
    private fun Root() {
        val session: SessionViewModel = viewModel(factory = factory { SessionViewModel(authRepo, profileRepo) })
        val state by session.state.collectAsStateWithLifecycle()

        when (val s = state) {
            SessionState.Loading -> SessionLoadingScreen()
            SessionState.Error -> SessionErrorScreen(onRetry = session::reload, onSignOut = session::signOut)
            SessionState.SignedOut -> {
                val vm: AuthViewModel = viewModel(factory = factory { AuthViewModel(authRepo) })
                val ui by vm.state.collectAsStateWithLifecycle()
                AuthScreen(ui, vm::onEmail, vm::onPassword, vm::submit, vm::toggleMode)
            }
            is SessionState.NeedsProfile -> {
                val vm: ProfileSetupViewModel = viewModel(
                    key = "profile-${s.uid}",
                    factory = factory { ProfileSetupViewModel(s.uid, profileRepo) },
                )
                val ui by vm.state.collectAsStateWithLifecycle()
                LaunchedEffect(ui.done) { if (ui.done) session.reload() }
                ProfileSetupScreen(
                    state = ui,
                    onUsername = vm::onUsername, onDisplayName = vm::onDisplayName, onBio = vm::onBio,
                    onDay = vm::onDay, onMonth = vm::onMonth, onYear = vm::onYear,
                    onSubmit = vm::submit, onSignOut = session::signOut,
                )
            }
            is SessionState.Ready -> Ready(s.profile, session::signOut)
        }
    }

    @Composable
    private fun Ready(profile: UserProfile, onSignOut: () -> Unit) {
        val uid = profile.uid
        val homeVm: HomeViewModel = viewModel(key = "home-$uid", factory = factory { HomeViewModel(roomsRepo) })
        var roomId by rememberSaveable { mutableStateOf<String?>(null) }
        // عدّاد دخول يجعل كل دخول للغرفة ViewModel جديداً (لا حالة قديمة من زيارة سابقة)
        var entry by rememberSaveable { mutableStateOf(0) }
        // كلمة سر الغرفة الخاصة تبقى في الذاكرة فقط (لا تُحفظ في الحالة المحفوظة)
        var roomPassword by remember { mutableStateOf<String?>(null) }

        val current = roomId
        if (current == null) {
            val accountVm: AccountViewModel = viewModel(
                key = "account-$uid",
                factory = factory { AccountViewModel(profile, profileRepo) },
            )
            MainScaffold(
                home = {
                    val ui by homeVm.state.collectAsStateWithLifecycle()
                    val filter by homeVm.filter.collectAsStateWithLifecycle()
                    val create by homeVm.create.collectAsStateWithLifecycle()
                    HomeScreen(
                        myUid = uid,
                        state = ui,
                        filter = filter,
                        create = create,
                        onRetry = homeVm::refresh,
                        onSelectCountry = homeVm::setCountry,
                        onSelectSort = homeVm::setSort,
                        onOpenRoom = { id, password -> entry++; roomPassword = password; roomId = id },
                        onCreate = { title, topic, country, password ->
                            homeVm.createRoom(title, topic, country, password) { id -> entry++; roomPassword = null; roomId = id }
                        },
                        onClearCreateError = homeVm::clearCreateError,
                    )
                },
                account = {
                    val ui by accountVm.state.collectAsStateWithLifecycle()
                    AccountScreen(
                        state = ui,
                        onEdit = accountVm::startEdit,
                        onCancel = accountVm::cancelEdit,
                        onSave = accountVm::save,
                        onDisplayName = accountVm::onDisplayName,
                        onBio = accountVm::onBio,
                        onCountry = accountVm::onCountry,
                        onSignOut = onSignOut,
                    )
                },
            )
        } else {
            val vm: RoomViewModel = viewModel(
                key = "room-$current-$entry",
                factory = factory {
                    RoomViewModel(
                        current, roomsRepo, profileRepo, LiveKitVoiceEngine(applicationContext),
                        password = roomPassword,
                    )
                },
            )
            val ui by vm.state.collectAsStateWithLifecycle()
            RoomScreen(
                myUid = uid,
                state = ui,
                onSetMic = vm::setMic,
                onRequestSeat = vm::requestSeat,
                onLeaveSeat = vm::leaveSeat,
                onLeaveRoom = vm::leaveRoom,
                onClose = vm::closeRoom,
                onDecide = vm::decide,
                onMute = vm::mute,
                onKick = vm::kick,
                onReconnect = vm::reconnect,
                onClearError = vm::clearError,
                onSendMessage = vm::sendMessage,
                onExit = {
                    roomId = null
                    roomPassword = null
                    homeVm.refresh()
                },
            )
        }
    }
}
