import SwiftUI

struct SettingsView: View {
    @Environment(AppState.self) private var appState
    @State private var serverURL: String = ""
    @State private var isLocking = false

    var body: some View {
        Form {
            Section("Server") {
                LabeledContent("URL", value: serverURL)
            }
            Section("Vault") {
                Button(role: .destructive) {
                    Task {
                        isLocking = true
                        await appState.lock()
                        isLocking = false
                    }
                } label: {
                    HStack {
                        Image(systemName: "lock.fill")
                        Text("Lock vault")
                    }
                }
                .disabled(isLocking)
            }
        }
        .navigationTitle("Settings")
        .task {
            serverURL = (try? await KeychainService.shared.loadServerURL()) ?? ""
        }
    }
}
