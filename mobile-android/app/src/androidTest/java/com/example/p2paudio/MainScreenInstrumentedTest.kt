package com.example.p2paudio

import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MainScreenInstrumentedTest {

    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun initialScreenShowsEntryActions() {
        composeRule.onAllNodesWithTag("entry_actions_card").assertCountEquals(1)
        composeRule.onAllNodesWithTag("entry_start_sender_button").assertCountEquals(1)
        composeRule.onAllNodesWithTag("entry_start_listener_button").assertCountEquals(1)
    }

    @Test
    fun legacyRoleSelectorsAreRemoved() {
        composeRule.onAllNodesWithTag("role_sharer_button").assertCountEquals(0)
        composeRule.onAllNodesWithTag("role_listener_button").assertCountEquals(0)
    }

    @Test
    fun listenerFlowShowsPayloadInput() {
        composeRule.onNodeWithTag("entry_start_listener_button").performClick()

        composeRule.onAllNodesWithTag("listener_scan_step_card").assertCountEquals(1)
        composeRule.onAllNodesWithTag("listener_payload_input").assertCountEquals(1)
    }
}
