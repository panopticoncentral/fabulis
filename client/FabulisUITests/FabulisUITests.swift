import XCTest

final class FabulisUITests: XCTestCase {
    override func setUpWithError() throws { continueAfterFailure = false }

    @MainActor private func launch(_ extra: [String] = []) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments = ["-ui-testing"] + extra
        app.launch()
        #if targetEnvironment(macCatalyst)
        resizeMacWindow(app, width: 1024)
        #endif
        return app
    }

    @MainActor private func destination(_ name: String, in app: XCUIApplication) {
        let tab = app.tabBars.buttons[name]
        if tab.exists { tab.activateElement() } else {
            #if targetEnvironment(macCatalyst)
            if !app.buttons[name].firstMatch.exists { app.buttons["Sidebar"].firstMatch.click() }
            #endif
            app.buttons[name].firstMatch.activateElement()
        }
    }

    @MainActor private func capture(_ name: String, app: XCUIApplication) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    @MainActor func testDraftCompositionSurvivesBrowsing() {
        let app = launch()
        XCTAssertTrue(app.staticTexts["Lantern draft"].firstMatch.waitForExistence(timeout: 10))
        app.staticTexts["Lantern draft"].firstMatch.activateElement()
        let composer = app.textViews["Write a prompt…"]
        XCTAssertTrue(composer.waitForExistence(timeout: 5))
        composer.activateElement()
        composer.typeText("Keep this unfinished thought")
        let unfinishedText = composer.value as? String
        XCTAssertTrue(unfinishedText?.contains("Keep this unfinished thought") == true)
        capture("Draft composer", app: app)
        destination("Stories", in: app)
        destination("Drafts", in: app)
        if !composer.exists { app.staticTexts["Lantern draft"].firstMatch.activateElement() }
        // The software keyboard can commit a trailing space when focus leaves.
        XCTAssertEqual((composer.value as? String)?.trimmingCharacters(in: .whitespacesAndNewlines),
                       unfinishedText?.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    @MainActor func testReaderHasVisibleListeningAndConversationOption() {
        let app = launch()
        destination("Stories", in: app)
        XCTAssertTrue(app.staticTexts["Nightfall"].firstMatch.waitForExistence(timeout: 5))
        app.staticTexts["Nightfall"].firstMatch.activateElement()
        app.staticTexts["The Lantern Keeper"].firstMatch.activateElement()
        XCTAssertTrue(app.buttons["Listen"].firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(app.staticTexts["PROMPT"].exists)
        capture("Story reader", app: app)
        let prose = app.staticTexts["The last light in the village belonged to Mara."].firstMatch
        XCTAssertTrue(prose.exists && prose.isHittable)
        #if targetEnvironment(macCatalyst)
        // Catalyst exposes toolbar menus as native “More” menu buttons.
        app.menuButtons.matching(identifier: "More").allElementsBoundByIndex.last?.click()
        app.menuItems["Conversation"].click()
        #else
        app.descendants(matching: .any)["reading-options"].firstMatch.activateElement()
        app.buttons["Conversation"].activateElement()
        #endif
        XCTAssertTrue(app.staticTexts["Write a quiet story about a lighthouse keeper."].firstMatch.waitForExistence(timeout: 5))
        capture("Conversation", app: app)
    }

    #if targetEnvironment(macCatalyst)
    @MainActor func testSidebarRowsRespondAcrossTheirWidthAfterLaunch() {
        // Do not resize or activate another control first: the first click
        // should navigate, including when it lands beside a row's text.
        for _ in 0..<3 {
            let app = XCUIApplication()
            app.launchArguments = ["-ui-testing"]
            app.launch()
            for name in ["Stories", "Drafts", "Prompts", "One-liners", "Tropes", "Stories"] {
                let row = app.buttons[name].firstMatch
                XCTAssertTrue(row.waitForExistence(timeout: 5))
                // A broken row reports only its text as its accessibility frame.
                // Use a point beyond that text, still inside the visible sidebar.
                row.coordinate(withNormalizedOffset: CGVector(dx: 0, dy: 0.5))
                    .withOffset(CGVector(dx: 150, dy: 0)).click()
                let selected = XCTNSPredicateExpectation(predicate: NSPredicate(format: "selected == true"), object: row)
                XCTAssertEqual(XCTWaiter.wait(for: [selected], timeout: 3), .completed, "Clicking the full \(name) row should select it")
            }
            app.terminate()
        }
    }

    @MainActor private func resizeMacWindow(_ app: XCUIApplication, width: CGFloat) {
        let window = app.windows["SceneWindow"].firstMatch
        XCTAssertTrue(window.waitForExistence(timeout: 5))
        let topLeft = window.coordinate(withNormalizedOffset: .zero)
        window.coordinate(withNormalizedOffset: CGVector(dx: 1, dy: 1))
            .withOffset(CGVector(dx: -2, dy: -2))
            .click(forDuration: 0.2, thenDragTo: topLeft.withOffset(CGVector(dx: width - 2, dy: 700)))
    }

    @MainActor func testStorySelectionOpensReaderInNarrowMacWindow() {
        let app = launch()
        destination("Stories", in: app)
        XCTAssertTrue(app.staticTexts["Nightfall"].firstMatch.waitForExistence(timeout: 5))
        app.staticTexts["Nightfall"].firstMatch.click()
        resizeMacWindow(app, width: 900)
        XCTAssertLessThan(app.windows["SceneWindow"].firstMatch.frame.width, 1000)
        let story = app.staticTexts["The Lantern Keeper"].firstMatch
        XCTAssertTrue(story.waitForExistence(timeout: 5))
        story.click()
        let prose = app.staticTexts["The last light in the village belonged to Mara."].firstMatch
        XCTAssertTrue(prose.waitForExistence(timeout: 5))
        XCTAssertTrue(prose.isHittable, "Selecting a story must reveal the reader, including in a narrow window")
    }
    #endif

    @MainActor func testSavingNewStoryProvidesOpenStoryAction() {
        let app = launch()
        XCTAssertTrue(app.staticTexts["Lantern draft"].firstMatch.waitForExistence(timeout: 10))
        app.staticTexts["Lantern draft"].firstMatch.activateElement()
        let saveButton = app.buttons["Save to Library…"].firstMatch
        XCTAssertTrue(saveButton.waitForExistence(timeout: 5))
        XCTAssertTrue(saveButton.isEnabled, "A loaded draft with messages should enable Save to Library")
        saveButton.activateElement()
        XCTAssertTrue(app.buttons["Create Story"].waitForExistence(timeout: 5))
        capture("Save to library", app: app)
        app.buttons["Create Story"].activateElement()
        XCTAssertTrue(app.buttons["Open Story"].waitForExistence(timeout: 5))
    }

    @MainActor func testSettingsFailureOffersRetryInsteadOfEditableDefaults() {
        let app = launch(["-ui-settings-failure"])
        app.descendants(matching: .any)["library-options"].firstMatch.activateElement()
        app.buttons["Settings…"].activateElement()
        app.staticTexts["Narration"].firstMatch.activateElement()
        XCTAssertTrue(app.buttons["Retry"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.sliders.firstMatch.exists)
        capture("Settings recovery", app: app)
    }
    @MainActor func testPromptEditingExplainsDestructiveRegeneration() {
        let app = launch()
        XCTAssertTrue(app.staticTexts["Lantern draft"].firstMatch.waitForExistence(timeout: 10))
        app.staticTexts["Lantern draft"].firstMatch.activateElement()
        #if !targetEnvironment(macCatalyst)
        app.scrollViews.firstMatch.swipeDown()
        #endif
        app.buttons["Message actions"].firstMatch.activateElement()
        app.buttons["Edit"].firstMatch.activateElement()
        XCTAssertTrue(app.textViews["Edit prompt"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Save Changes"].exists)
        capture("Edit prompt", app: app)
        app.buttons["Regenerate from Here…"].activateElement()
        XCTAssertTrue(app.alerts["Regenerate from here?"].waitForExistence(timeout: 5))
        app.alerts.buttons["Cancel"].activateElement()
        XCTAssertTrue(app.textViews["Edit prompt"].exists)
    }

    @MainActor func testLargeTextReader() {
        let app = launch(["-UIPreferredContentSizeCategoryName", "UICTContentSizeCategoryAccessibilityXXXL"])
        destination("Stories", in: app)
        XCTAssertTrue(app.staticTexts["Nightfall"].firstMatch.waitForExistence(timeout: 5))
        app.staticTexts["Nightfall"].firstMatch.activateElement()
        app.staticTexts["The Lantern Keeper"].firstMatch.activateElement()
        XCTAssertTrue(app.buttons["Listen"].firstMatch.waitForExistence(timeout: 5))
        capture("Reader with accessibility text", app: app)
    }

}

private extension XCUIElement {
    @MainActor func activateElement() {
        #if targetEnvironment(macCatalyst)
        click()
        #else
        tap()
        #endif
    }
}
