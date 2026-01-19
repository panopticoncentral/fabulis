//
//  ContentView.swift
//  Fabulis
//
//  Created by Paul Vick on 9/2/25.
//

import SwiftUI
import SwiftData

struct ContentView: View {
    @Environment(\.modelContext) private var modelContext
    @State private var hasCompletedOnboarding = false
    @State private var isCheckingAuth = true
    @State private var libraryViewModel = LibraryViewModel()

    var body: some View {
        Group {
            if isCheckingAuth {
                ProgressView("Loading...")
            } else if hasCompletedOnboarding {
                MainTabView()
                    .environment(libraryViewModel)
            } else {
                OnboardingView(onComplete: {
                    hasCompletedOnboarding = true
                    libraryViewModel.seedDefaultStorytellers(modelContext: modelContext)
                })
            }
        }
        .task {
            let keychain = KeychainService.shared
            hasCompletedOnboarding = await keychain.hasAPIKey()

            if hasCompletedOnboarding {
                libraryViewModel.seedDefaultStorytellers(modelContext: modelContext)
            }

            isCheckingAuth = false
        }
    }
}

struct MainTabView: View {
    var body: some View {
        LibraryView()
    }
}

#Preview {
    ContentView()
        .modelContainer(for: [Storyteller.self, Story.self, StorySegment.self], inMemory: true)
}
