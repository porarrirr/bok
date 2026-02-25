package com.example.p2paudio

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
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
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.TextButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.lifecycle.lifecycleScope
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.qr.QrCodeEncoder
import com.example.p2paudio.service.AudioSendService
import com.example.p2paudio.ui.MainUiState
import com.example.p2paudio.ui.MainViewModel
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
            val captureStarted = viewModel.onProjectionPermissionResult(result.data)
            if (captureStarted) {
                startForegroundSendService()
            }
        } else {
            viewModel.onProjectionPermissionResult(null)
        }
    }

    private val qrScanLauncher = registerForActivityResult(ScanContract()) { result ->
        val contents = result.contents ?: return@registerForActivityResult
        when (pendingScanTarget) {
            ScanTarget.OFFER -> viewModel.createAnswerFromOffer(contents)
            ScanTarget.ANSWER -> viewModel.applyAnswer(contents)
            ScanTarget.NONE -> Unit
        }
        pendingScanTarget = ScanTarget.NONE
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        lifecycleScope.launch {
            viewModel.commands.collect { command ->
                when (command) {
                    is MainViewModel.UiCommand.RequestProjectionPermission -> {
                        projectionLauncher.launch(command.captureIntent)
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
                        onStartSender = viewModel::requestProjectionPermission,
                        onScanOffer = {
                            pendingScanTarget = ScanTarget.OFFER
                            launchQrScanner()
                        },
                        onScanAnswer = {
                            pendingScanTarget = ScanTarget.ANSWER
                            launchQrScanner()
                        },
                        onSubmitOfferText = viewModel::createAnswerFromOffer,
                        onSubmitAnswerText = viewModel::applyAnswer,
                        onStop = {
                            stopService(Intent(this, AudioSendService::class.java))
                            viewModel.stopSession()
                        }
                    )
                }
            }
        }
    }

    private fun launchQrScanner() {
        val options = ScanOptions()
            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            .setPrompt("Scan QR payload")
            .setBeepEnabled(false)
            .setCameraId(0)
            .setOrientationLocked(false)
        qrScanLauncher.launch(options)
    }

    private fun startForegroundSendService() {
        val intent = Intent(this, AudioSendService::class.java)
        startForegroundService(intent)
    }

    private enum class ScanTarget {
        NONE,
        OFFER,
        ANSWER
    }
}

@Composable
private fun MainScreen(
    uiState: MainUiState,
    onStartSender: () -> Unit,
    onScanOffer: () -> Unit,
    onScanAnswer: () -> Unit,
    onSubmitOfferText: (String) -> Unit,
    onSubmitAnswerText: (String) -> Unit,
    onStop: () -> Unit
) {
    var offerInput by rememberSaveable { mutableStateOf("") }
    var answerInput by rememberSaveable { mutableStateOf("") }
    var transientMessage by remember { mutableStateOf("") }
    val clipboardManager = LocalClipboardManager.current

    val offerQr = remember(uiState.offerPayload) {
        uiState.offerPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }
    val answerQr = remember(uiState.answerPayload) {
        uiState.answerPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }

    LaunchedEffect(transientMessage) {
        if (transientMessage.isNotBlank()) {
            delay(1800)
            transientMessage = ""
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Text(
            text = "P2P Audio Bridge",
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.SemiBold
        )
        Text(
            text = "Pair devices via QR or payload text on the same network.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )

        SessionStatusCard(uiState = uiState)

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Button(
                modifier = Modifier.weight(1f),
                onClick = onStartSender
            ) {
                Text("Start Sender")
            }
            FilledTonalButton(
                modifier = Modifier.weight(1f),
                onClick = onStop
            ) {
                Text("Stop Session")
            }
        }

        if (transientMessage.isNotBlank()) {
            Text(
                text = transientMessage,
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary
            )
        }

        FlowCard(
            title = "Receiver Flow",
            description = "Scan or paste a sender offer. Then share the generated answer back.",
            inputLabel = "Offer payload",
            inputValue = offerInput,
            onInputValueChange = { offerInput = it },
            onScanClick = onScanOffer,
            onSubmitClick = { onSubmitOfferText(offerInput.trim()) },
            scanButtonLabel = "Scan Offer QR",
            submitButtonLabel = "Use Offer Text",
            payloadTitle = "Generated answer payload",
            payloadValue = uiState.answerPayload,
            payloadQr = answerQr,
            payloadQrDescription = "Answer QR",
            onCopyPayload = {
                clipboardManager.setText(AnnotatedString(uiState.answerPayload))
                transientMessage = "Answer payload copied."
            }
        )

        FlowCard(
            title = "Sender Flow",
            description = "Start sender to create an offer. Then import receiver answer.",
            inputLabel = "Answer payload",
            inputValue = answerInput,
            onInputValueChange = { answerInput = it },
            onScanClick = onScanAnswer,
            onSubmitClick = { onSubmitAnswerText(answerInput.trim()) },
            scanButtonLabel = "Scan Answer QR",
            submitButtonLabel = "Use Answer Text",
            payloadTitle = "Generated offer payload",
            payloadValue = uiState.offerPayload,
            payloadQr = offerQr,
            payloadQrDescription = "Offer QR",
            onCopyPayload = {
                clipboardManager.setText(AnnotatedString(uiState.offerPayload))
                transientMessage = "Offer payload copied."
            }
        )
    }
}

@Composable
private fun SessionStatusCard(uiState: MainUiState) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Text(
                text = "Connection Status",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = uiState.streamState.toReadableLabel(),
                style = MaterialTheme.typography.labelLarge,
                color = statusColor(uiState.streamState)
            )
            Text(
                text = uiState.statusMessage,
                style = MaterialTheme.typography.bodyMedium
            )
            if (uiState.activeSessionId.isNotBlank()) {
                Text(
                    text = "Session ID",
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
            uiState.failure?.let { failure ->
                HorizontalDivider()
                Text(
                    text = "Failure: ${failure.code.name}",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.error
                )
            }
        }
    }
}

@Composable
private fun FlowCard(
    title: String,
    description: String,
    inputLabel: String,
    inputValue: String,
    onInputValueChange: (String) -> Unit,
    onScanClick: () -> Unit,
    onSubmitClick: () -> Unit,
    scanButtonLabel: String,
    submitButtonLabel: String,
    payloadTitle: String,
    payloadValue: String,
    payloadQr: android.graphics.Bitmap?,
    payloadQrDescription: String,
    onCopyPayload: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth()
    ) {
        Column(
            modifier = Modifier.padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = description,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            OutlinedTextField(
                value = inputValue,
                onValueChange = onInputValueChange,
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(min = 112.dp),
                label = { Text(inputLabel) },
                supportingText = { Text("Paste full payload string here.") }
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                FilledTonalButton(
                    modifier = Modifier.weight(1f),
                    onClick = onScanClick
                ) {
                    Text(scanButtonLabel)
                }
                Button(
                    modifier = Modifier.weight(1f),
                    enabled = inputValue.isNotBlank(),
                    onClick = onSubmitClick
                ) {
                    Text(submitButtonLabel)
                }
            }

            if (payloadValue.isNotBlank()) {
                HorizontalDivider()
                Text(
                    text = payloadTitle,
                    style = MaterialTheme.typography.labelLarge
                )
                OutlinedTextField(
                    value = payloadValue,
                    onValueChange = {},
                    readOnly = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = 96.dp),
                    maxLines = 5
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    TextButton(onClick = onCopyPayload) {
                        Text("Copy payload")
                    }
                    Text(
                        text = "${payloadValue.length} chars",
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
    AudioStreamState.STREAMING -> MaterialTheme.colorScheme.primary
    AudioStreamState.FAILED -> MaterialTheme.colorScheme.error
    AudioStreamState.CONNECTING,
    AudioStreamState.CAPTURING -> MaterialTheme.colorScheme.tertiary
    else -> MaterialTheme.colorScheme.onSurfaceVariant
}

private fun AudioStreamState.toReadableLabel(): String = when (this) {
    AudioStreamState.IDLE -> "Idle"
    AudioStreamState.CAPTURING -> "Capturing"
    AudioStreamState.CONNECTING -> "Connecting"
    AudioStreamState.STREAMING -> "Streaming"
    AudioStreamState.INTERRUPTED -> "Interrupted"
    AudioStreamState.FAILED -> "Failed"
    AudioStreamState.ENDED -> "Ended"
}
