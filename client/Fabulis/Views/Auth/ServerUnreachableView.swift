import SwiftUI

struct ServerUnreachableView: View {
    @Environment(AppState.self) private var appState
    let serverURL: String
    let message: String
    @State private var isRetrying = false
    @State private var showingResetConfirm = false

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Image(systemName: "wifi.exclamationmark")
                    .font(.system(size: 56))
                    .foregroundStyle(.tint)
                Text("Can't reach the server").font(.title.bold())
                if !serverURL.isEmpty {
                    Text(serverURL).font(.callout).foregroundStyle(.secondary)
                }
                Text(message)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .multilineTextAlignment(.center)
                Button { Task { await retry() } } label: {
                    Group { if isRetrying { ProgressView() } else { Text("Try again") } }
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(isRetrying)
                Button("Use a different server") {
                    showingResetConfirm = true
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .disabled(isRetrying)
            }
            .frame(maxWidth: 420)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
            .confirmationDialog("Disconnect from this server?",
                                isPresented: $showingResetConfirm,
                                titleVisibility: .visible) {
                Button("Disconnect", role: .destructive) {
                    Task { await appState.resetServer() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("You'll need to re-enter the server URL and password to reconnect.")
            }
        }
    }

    private func retry() async {
        isRetrying = true
        defer { isRetrying = false }
        await appState.bootstrap()
    }
}
