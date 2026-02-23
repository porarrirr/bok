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
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import com.example.p2paudio.qr.QrCodeEncoder
import com.example.p2paudio.service.AudioSendService
import com.example.p2paudio.ui.MainUiState
import com.example.p2paudio.ui.MainViewModel
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
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
    var offerInput by remember { mutableStateOf("") }
    var answerInput by remember { mutableStateOf("") }

    val offerQr = remember(uiState.offerPayload) {
        uiState.offerPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }
    val answerQr = remember(uiState.answerPayload) {
        uiState.answerPayload.takeIf { it.isNotBlank() }?.let { QrCodeEncoder.generate(it) }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(16.dp)
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text(text = "State: ${uiState.streamState}")
        Text(text = uiState.statusMessage)
        if (uiState.activeSessionId.isNotBlank()) {
            Text(text = "Session: ${uiState.activeSessionId}")
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onStartSender) { Text("Start sender") }
            Button(onClick = onStop) { Text("Stop") }
        }

        Spacer(modifier = Modifier.height(8.dp))
        Text(text = "Receiver")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onScanOffer) { Text("Scan offer QR") }
            Button(onClick = { if (offerInput.isNotBlank()) onSubmitOfferText(offerInput) }) {
                Text("Submit offer text")
            }
        }
        BasicTextField(
            value = offerInput,
            onValueChange = { offerInput = it },
            modifier = Modifier
                .fillMaxWidth()
                .height(120.dp)
        )

        offerQr?.let {
            Text("Sender Offer QR")
            Image(bitmap = it.asImageBitmap(), contentDescription = "Offer QR")
        }

        Spacer(modifier = Modifier.height(8.dp))
        Text(text = "Sender")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onScanAnswer) { Text("Scan answer QR") }
            Button(onClick = { if (answerInput.isNotBlank()) onSubmitAnswerText(answerInput) }) {
                Text("Submit answer text")
            }
        }
        BasicTextField(
            value = answerInput,
            onValueChange = { answerInput = it },
            modifier = Modifier
                .fillMaxWidth()
                .height(120.dp)
        )

        answerQr?.let {
            Text("Receiver Answer QR")
            Image(bitmap = it.asImageBitmap(), contentDescription = "Answer QR")
        }
    }
}
