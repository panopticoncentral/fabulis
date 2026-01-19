//
//  FabulisApp.swift
//  Fabulis
//
//  Created by Paul Vick on 9/2/25.
//

import SwiftUI
import SwiftData

@main
struct FabulisApp: App {
    var sharedModelContainer: ModelContainer = {
        let schema = Schema([
            Storyteller.self,
            Story.self,
            StorySegment.self
        ])
        let modelConfiguration = ModelConfiguration(
            schema: schema,
            isStoredInMemoryOnly: false,
            cloudKitDatabase: .none
        )

        do {
            return try ModelContainer(for: schema, configurations: [modelConfiguration])
        } catch {
            fatalError("Could not create ModelContainer: \(error)")
        }
    }()

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
        .modelContainer(sharedModelContainer)
    }
}
