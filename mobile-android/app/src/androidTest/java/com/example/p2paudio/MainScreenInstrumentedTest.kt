package com.example.p2paudio

import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
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
        composeRule.onAllNodesWithTag("entry_scan_init_button").assertCountEquals(1)
    }

    @Test
    fun legacyRoleSelectorsAreRemoved() {
        composeRule.onAllNodesWithTag("role_sharer_button").assertCountEquals(0)
        composeRule.onAllNodesWithTag("role_listener_button").assertCountEquals(0)
    }

    @Test
    fun manualInputUiIsRemoved() {
        composeRule.onAllNodesWithTag("sharer_manual_toggle").assertCountEquals(0)
        composeRule.onAllNodesWithTag("listener_manual_toggle").assertCountEquals(0)
        composeRule.onAllNodesWithTag("sharer_manual_input").assertCountEquals(0)
        composeRule.onAllNodesWithTag("listener_manual_input").assertCountEquals(0)
    }
}
