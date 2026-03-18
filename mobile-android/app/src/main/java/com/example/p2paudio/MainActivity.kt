package com.example.p2paudio

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import com.example.p2paudio.logging.AppLogger
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.ConnectionDiagnostics
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.NetworkPathType
import com.example.p2paudio.qr.QrCodeEncoder
import com.example.p2paudio.service.AudioSendService
import com.example.p2paudio.ui.MainUiState
import com.example.p2paudio.ui.MainViewModel
import com.example.p2paudio.ui.SetupStep
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private val viewModel by viewModels<MainViewModel>()

    private var pendingScanTarget: ScanTarget = ScanTarget.NONE

    private val projectionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            AppLogger.i("MainActivity", "projection_permission_granted", "Projection permission granted")
            viewModel.onProjectionPermissionResult(result.data)
        } else {
            AppLogger.w("MainActivity", "projection_permission_denied", "Projection permission denied")
            viewModel.onProjectionPermissionResult(null)
        }
    }

    private val recordAudioPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            AppLogger.i("MainActivity", "record_permission_granted", "RECORD_AUDIO permission granted")
        } else {
            AppLogger.w("MainActivity", "record_permission_denied", "RECORD_AUDIO permission denied")
        }
        viewModel.onRecordAudioPermissionResult(granted)
    }

    private val qrScanLauncher = registerForActivityResult(ScanContract()) { result ->
        val contents = result.contents ?: return@registerForActivityResult
        AppLogger.d(
            "MainActivity",
            "qr_scan_result",
            "QR payload received",
            context = mapOf("target" to pendingScanTarget.name, "length" to contents.length)
        )
        when (pendingScanTarget) {
            ScanTarget.INIT -> viewModel.createConfirmFromInit(contents)
            ScanTarget.CONFIRM -> viewModel.applyConfirm(contents)
            ScanTarget.NONE -> Unit
        }
        pendingScanTarget = ScanTarget.NONE
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        lifecycleScope.launch {
            viewModel.commands.collect { command ->
                when (command) {
                    MainViewModel.UiCommand.RequestRecordAudioPermission -> {
                        recordAudioPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
                    }

                    is MainViewModel.UiCommand.RequestProjectionPermission -> {
                        projectionLauncher.launch(command.captureIntent)
                    }

                    is MainViewModel.UiCommand.StartProjectionService -> {
                        runCatching {
                            startForegroundSendService(command.permissionResultData)
                        }.onFailure { error ->
                            viewModel.onProjectionServiceStartFailed(error)
                        }
                    }

                    MainViewModel.UiCommand.StopProjectionService -> {
                        stopForegroundSendService()
                    }
                }
            }
        }

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val uiState by viewModel.uiState.collectAsState(initial = MainUiState())
                    MainScreen(
                        uiState = uiState,
                        onStartSender = viewModel::startSenderFlowRequested,
                        onStartListenerScan = {
                            viewModel.beginListenerFlow()
                            pendingScanTarget = ScanTarget.INIT
                            launchQrScanner()
                        },
                        onScanConfirm = {
                            pendingScanTarget = ScanTarget.CONFIRM
                            launchQrScanner()
                        },
                        onVerificationMatch = viewModel::approveVerificationAndConnect,
                        onVerificationMismatch = viewModel::rejectVerificationAndRestart,
                        onStop = viewModel::stopSession
                    )
                }
            }
        }
    }

    private fun launchQrScanner() {
        AppLogger.i(
            "MainActivity",
            "qr_scanner_launch",
            "Launching QR scanner",
            context = mapOf("target" to pendingScanTarget.name)
        )
        val options = ScanOptions()
            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            .setPrompt(getString(R.string.scanner_prompt))
            .setBeepEnabled(false)
            .setCameraId(0)
            .setOrientationLocked(false)
        qrScanLauncher.launch(options)
    }

    private fun startForegroundSendService(permissionResultData: Intent) {
        AppLogger.i("MainActivity", "start_foreground_service", "Starting AudioSendService")
        val intent = Intent(this, AudioSendService::class.java).apply {
            action = AudioSendService.ACTION_START_CAPTURE
            putExtra(AudioSendService.EXTRA_PROJECTION_DATA, permissionResultData)
        }
        startForegroundService(intent)
    }

    private fun stopForegroundSendService() {
        AppLogger.i("MainActivity", "stop_foreground_service", "Stopping AudioSendService")
        stopService(Intent(this, AudioSendService::class.java))
    }

    private enum class ScanTarget {
        NONE,
        INIT,
        CONFIRM
    }
}

@Composable
private fun MainScreen(
    uiState: MainUiState,
    onStartSender: () -> Unit,
    onStartListenerScan: () -> Unit,
    onScanConfirm: () -> Unit,
    onVerificationMatch: () -> Unit,
    onVerificationMismatch: () -> Unit,
    onStop: () -> Unit
) {
    var transientMessage by remember { mutableStateOf("") }
    val clipboardManager = LocalClipboardManager.current
    val initCopiedText = stringResource(R.string.flow_sender_payload_copied)
    val confirmCopiedText = stringResource(R.string.flow_receiver_payload_copied)
    val canStartNewFlow = uiState.setupStep == SetupStep.ENTRY && uiState.streamState == AudioStreamState.IDLE
    val canStopSession = !canStartNewFlow
    val showSetupCards = when (uiState.streamState) {
        AudioStreamState.STREAMING,
        AudioStreamState.INTERRUPTED,
        AudioStreamState.FAILED,
        AudioStreamState.ENDED -> false
        else -> true
    }

    val initQr = remember(uiState.initPayload) {
        uiState.initPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }
    val confirmQr = remember(uiState.confirmPayload) {
        uiState.confirmPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }

    LaunchedEffect(transientMessage) {
        if (transientMessage.isNotBlank()) {
            delay(1800)
            transientMessage = ""
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.verticalGradient(
                    colors = listOf(
                        Color(0xFFF4F8FF),
                        Color(0xFFFFF5EC)
                    )
                )
            )
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 18.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            HeaderCard()
            SessionStatusCard(uiState = uiState)

            EntryActionsCard(
                onStartSender = onStartSender,
                onStartListenerScan = onStartListenerScan,
                onStop = onStop,
                canStartNewFlow = canStartNewFlow,
                canStopSession = canStopSession
            )

            if (transientMessage.isNotBlank()) {
                Text(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 4.dp),
                    text = transientMessage,
                    style = MaterialTheme.typography.labelLarge,
                    color = Color(0xFF145A72)
                )
            }

            if (showSetupCards) {
                when (uiState.setupStep) {
                    SetupStep.ENTRY -> Unit
                    SetupStep.PATH_DIAGNOSING -> StepCard(
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("path_diagnosing_step_card"),
                        number = 2,
                        title = stringResource(R.string.flow_diagnosing_title),
                        description = stringResource(R.string.flow_diagnosing_description)
                    ) {
                        Text(
                            text = stringResource(R.string.status_path_diagnosing),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }

                    SetupStep.SENDER_SHOW_INIT -> StepCard(
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("sender_init_step_card"),
                        number = 2,
                        title = stringResource(R.string.flow_sender_step_title),
                        description = stringResource(R.string.flow_sender_step_description)
                    ) {
                        if (uiState.initPayload.isBlank()) {
                            Text(
                                text = stringResource(R.string.flow_sender_waiting_code),
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        } else {
                            ConnectionCodeBlock(
                                payloadTitle = stringResource(R.string.flow_sender_payload_title),
                                payloadValue = uiState.initPayload,
                                payloadQr = initQr,
                                payloadQrDescription = stringResource(R.string.flow_sender_qr_description),
                                onCopyPayload = {
                                    clipboardManager.setText(AnnotatedString(uiState.initPayload))
                                    transientMessage = initCopiedText
                                }
                            )
                        }
                        FilledTonalButton(
                            modifier = Modifier
                                .fillMaxWidth()
                                .heightIn(min = 46.dp)
                                .testTag("scan_confirm_button"),
                            enabled = uiState.initPayload.isNotBlank(),
                            onClick = onScanConfirm
                        ) {
                            Text(stringResource(R.string.flow_sender_scan_button))
                        }
                    }

                    SetupStep.SENDER_VERIFY_CODE -> StepCard(
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("sender_verify_card"),
                        number = 3,
                        title = stringResource(R.string.flow_verification_title),
                        description = stringResource(R.string.flow_verification_description)
                    ) {
                        VerificationCodeBlock(code = uiState.verificationCode)
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            Button(
                                modifier = Modifier
                                    .weight(1f)
                                    .heightIn(min = 46.dp)
                                    .testTag("verification_match_button"),
                                onClick = onVerificationMatch
                            ) {
                                Text(stringResource(R.string.flow_verification_match))
                            }
                            FilledTonalButton(
                                modifier = Modifier
                                    .weight(1f)
                                    .heightIn(min = 46.dp)
                                    .testTag("verification_mismatch_button"),
                                onClick = onVerificationMismatch
                            ) {
                                Text(stringResource(R.string.flow_verification_mismatch))
                            }
                        }
                    }

                    SetupStep.LISTENER_SCAN_INIT -> StepCard(
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("listener_scan_step_card"),
                        number = 2,
                        title = stringResource(R.string.flow_receiver_step_title),
                        description = stringResource(R.string.flow_receiver_step_description)
                    ) {
                        Text(
                            text = stringResource(R.string.flow_receiver_waiting_code),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }

                    SetupStep.LISTENER_SHOW_CONFIRM -> StepCard(
                        modifier = Modifier
                            .fillMaxWidth()
                            .testTag("listener_confirm_step_card"),
                        number = 3,
                        title = stringResource(R.string.flow_receiver_confirm_title),
                        description = stringResource(R.string.flow_receiver_confirm_description)
                    ) {
                        ConnectionCodeBlock(
                            payloadTitle = stringResource(R.string.flow_receiver_payload_title),
                            payloadValue = uiState.confirmPayload,
                            payloadQr = confirmQr,
                            payloadQrDescription = stringResource(R.string.flow_receiver_qr_description),
                            onCopyPayload = {
                                clipboardManager.setText(AnnotatedString(uiState.confirmPayload))
                                transientMessage = confirmCopiedText
                            }
                        )
                        VerificationCodeBlock(code = uiState.verificationCode)
                    }
                }
            }
        }
    }
}

@Composable
private fun HeaderCard() {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Text(
                text = stringResource(R.string.main_title),
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.Bold
            )
            Text(
                text = stringResource(R.string.main_subtitle),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
private fun SessionStatusCard(uiState: MainUiState) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Text(
                text = stringResource(R.string.status_connection_title),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = stringResource(uiState.streamState.labelResId()),
                style = MaterialTheme.typography.labelLarge,
                color = statusColor(uiState.streamState)
            )
            Text(
                text = uiState.statusMessage,
                style = MaterialTheme.typography.bodyMedium
            )

            if (uiState.activeSessionId.isNotBlank()) {
                Text(
                    text = stringResource(R.string.status_session_id),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                SelectionContainer {
                    Text(
                        text = uiState.activeSessionId,
                        style = MaterialTheme.typography.bodyMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }

            if (uiState.connectionDiagnostics.hasContent()) {
                HorizontalDivider()
                Text(
                    text = stringResource(R.string.status_network_path_title),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                Text(
                    text = stringResource(uiState.connectionDiagnostics.pathType.labelResId()),
                    style = MaterialTheme.typography.bodyMedium
                )
                Text(
                    text = stringResource(
                        R.string.status_local_candidates_format,
                        uiState.connectionDiagnostics.localCandidatesCount
                    ),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                if (uiState.connectionDiagnostics.selectedCandidatePairType.isNotBlank()) {
                    Text(
                        text = stringResource(
                            R.string.status_selected_pair_format,
                            uiState.connectionDiagnostics.selectedCandidatePairType
                        ),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                if (uiState.connectionDiagnostics.failureHint.isNotBlank()) {
                    val hintText = uiState.connectionDiagnostics.localizedHintText()
                    Text(
                        text = stringResource(
                            R.string.status_failure_hint_format,
                            hintText
                        ),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error
                    )
                }
            }

            uiState.failure?.let { failure ->
                HorizontalDivider()
                Text(
                    text = stringResource(
                        R.string.status_failure_format,
                        stringResource(failure.code.labelResId())
                    ),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.error
                )
            }

            HorizontalDivider()
            Text(
                text = stringResource(R.string.status_next_action_title),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text(
                text = stringResource(recommendedActionRes(uiState)),
                style = MaterialTheme.typography.bodyMedium
            )
        }
    }
}

@Composable
private fun EntryActionsCard(
    onStartSender: () -> Unit,
    onStartListenerScan: () -> Unit,
    onStop: () -> Unit,
    canStartNewFlow: Boolean,
    canStopSession: Boolean
) {
    StepCard(
        modifier = Modifier
            .fillMaxWidth()
            .testTag("entry_actions_card"),
        number = 1,
        title = stringResource(R.string.flow_entry_title),
        description = stringResource(R.string.flow_entry_description)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Button(
                modifier = Modifier
                    .weight(1f)
                    .heightIn(min = 46.dp)
                    .testTag("entry_start_sender_button"),
                enabled = canStartNewFlow,
                onClick = onStartSender
            ) {
                Text(stringResource(R.string.action_start_sender))
            }
            Button(
                modifier = Modifier
                    .weight(1f)
                    .heightIn(min = 46.dp)
                    .testTag("entry_scan_init_button"),
                enabled = canStartNewFlow,
                onClick = onStartListenerScan
            ) {
                Text(stringResource(R.string.action_start_listener_scan))
            }
        }
        FilledTonalButton(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 46.dp),
            enabled = canStopSession,
            onClick = onStop
        ) {
            Text(stringResource(R.string.action_stop_session))
        }
    }
}

@Composable
private fun VerificationCodeBlock(code: String) {
    if (code.isBlank()) {
        return
    }
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(
            text = stringResource(R.string.flow_verification_code_label),
            style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold
        )
        Text(
            text = code,
            style = MaterialTheme.typography.headlineMedium,
            fontWeight = FontWeight.Bold
        )
    }
}

@Composable
private fun StepCard(
    modifier: Modifier = Modifier,
    number: Int,
    title: String,
    description: String,
    content: @Composable ColumnScope.() -> Unit
) {
    Card(modifier = modifier) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(24.dp)
                        .background(
                            color = MaterialTheme.colorScheme.primaryContainer,
                            shape = MaterialTheme.shapes.small
                        ),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = number.toString(),
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
                Text(
                    text = title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold
                )
            }

            Text(
                text = description,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            content()
        }
    }
}

@Composable
private fun ConnectionCodeBlock(
    payloadTitle: String,
    payloadValue: String,
    payloadQr: android.graphics.Bitmap?,
    payloadQrDescription: String,
    onCopyPayload: () -> Unit
) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            text = payloadTitle,
            style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold
        )
        SelectionContainer {
            Text(
                text = payloadValue,
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.fillMaxWidth()
            )
        }
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            TextButton(onClick = onCopyPayload) {
                Text(stringResource(R.string.flow_copy_payload))
            }
            Text(
                text = stringResource(R.string.flow_payload_chars, payloadValue.length),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        payloadQr?.let { qr ->
            QrBlock(
                qr = qr,
                description = payloadQrDescription,
                size = 220.dp
            )
        }
    }
}

@Composable
private fun QrBlock(
    qr: android.graphics.Bitmap,
    description: String,
    size: Dp
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.Center
    ) {
        Image(
            bitmap = qr.asImageBitmap(),
            contentDescription = description,
            modifier = Modifier.size(size)
        )
    }
}

@Composable
private fun statusColor(state: AudioStreamState) = when (state) {
    AudioStreamState.STREAMING -> Color(0xFF176C2E)
    AudioStreamState.FAILED -> MaterialTheme.colorScheme.error
    AudioStreamState.CONNECTING,
    AudioStreamState.CAPTURING -> Color(0xFFAD5D0C)
    else -> MaterialTheme.colorScheme.onSurfaceVariant
}

private fun recommendedActionRes(uiState: MainUiState): Int {
    if (uiState.streamState == AudioStreamState.STREAMING) {
        return R.string.status_next_action_connected
    }
    if (uiState.streamState == AudioStreamState.INTERRUPTED) {
        return R.string.status_next_action_recovering
    }
    if (uiState.streamState == AudioStreamState.CONNECTING && uiState.setupStep == SetupStep.ENTRY) {
        return R.string.status_next_action_connecting
    }
    uiState.failure?.let { failure ->
        return when (failure.code) {
            FailureCode.PERMISSION_DENIED -> R.string.status_next_action_permission
            FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED -> R.string.status_next_action_capture_not_supported
            FailureCode.USB_TETHER_UNAVAILABLE -> R.string.status_next_action_usb_enable_tethering
            FailureCode.USB_TETHER_DETECTED_BUT_NOT_REACHABLE -> R.string.status_next_action_usb_replug
            FailureCode.NETWORK_INTERFACE_NOT_USABLE -> R.string.status_next_action_check_interface
            else -> R.string.status_next_action_restart
        }
    }
    return when (uiState.setupStep) {
        SetupStep.ENTRY -> R.string.status_next_action_entry
        SetupStep.PATH_DIAGNOSING -> R.string.status_next_action_diagnosing
        SetupStep.SENDER_SHOW_INIT -> R.string.status_next_action_show_init
        SetupStep.SENDER_VERIFY_CODE -> R.string.status_next_action_verify
        SetupStep.LISTENER_SCAN_INIT -> R.string.status_next_action_scan_init
        SetupStep.LISTENER_SHOW_CONFIRM -> R.string.status_next_action_show_confirm
    }
}

private fun AudioStreamState.labelResId(): Int = when (this) {
    AudioStreamState.IDLE -> R.string.state_idle
    AudioStreamState.CAPTURING -> R.string.state_capturing
    AudioStreamState.CONNECTING -> R.string.state_connecting
    AudioStreamState.STREAMING -> R.string.state_streaming
    AudioStreamState.INTERRUPTED -> R.string.state_interrupted
    AudioStreamState.FAILED -> R.string.state_failed
    AudioStreamState.ENDED -> R.string.state_ended
}

private fun FailureCode.labelResId(): Int = when (this) {
    FailureCode.PERMISSION_DENIED -> R.string.failure_code_permission_denied
    FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED -> R.string.failure_code_audio_capture_not_supported
    FailureCode.WEBRTC_NEGOTIATION_FAILED -> R.string.failure_code_webrtc_negotiation_failed
    FailureCode.PEER_UNREACHABLE -> R.string.failure_code_peer_unreachable
    FailureCode.NETWORK_CHANGED -> R.string.failure_code_network_changed
    FailureCode.USB_TETHER_UNAVAILABLE -> R.string.failure_code_usb_tether_unavailable
    FailureCode.USB_TETHER_DETECTED_BUT_NOT_REACHABLE -> R.string.failure_code_usb_tether_not_reachable
    FailureCode.NETWORK_INTERFACE_NOT_USABLE -> R.string.failure_code_network_interface_not_usable
    FailureCode.SESSION_EXPIRED -> R.string.failure_code_session_expired
    FailureCode.INVALID_PAYLOAD -> R.string.failure_code_invalid_payload
}

private fun ConnectionDiagnostics.hasContent(): Boolean {
    return pathType != NetworkPathType.UNKNOWN ||
        localCandidatesCount > 0 ||
        selectedCandidatePairType.isNotBlank() ||
        failureHint.isNotBlank()
}

@Composable
private fun ConnectionDiagnostics.localizedHintText(): String = when (failureHint) {
    "usb_tether_check" -> stringResource(R.string.status_hint_usb_tether_check)
    "wifi_lan_check" -> stringResource(R.string.status_hint_wifi_check)
    "network_interface_check" -> stringResource(R.string.status_hint_network_interface_check)
    "peer_disconnected" -> stringResource(R.string.status_peer_disconnected)
    else -> failureHint
}

private fun NetworkPathType.labelResId(): Int = when (this) {
    NetworkPathType.WIFI_LAN -> R.string.status_network_path_wifi
    NetworkPathType.USB_TETHER -> R.string.status_network_path_usb
    NetworkPathType.UNKNOWN -> R.string.status_network_path_unknown
}
