import SwiftUI

struct ServerUnreachableView: View {
    @Environment(AppState.self) private var appState
    let serverURL: String
    let message: String
    @State private var isRetrying = false

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
                    .padding(.horizontal)
                Button { Task { await retry() } } label: {
                    Group { if isRetrying { ProgressView() } else { Text("Try again") } }
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(isRetrying)
                .padding(.horizontal)
                Button("Use a different server") {
                    Task { await appState.resetServer() }
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .disabled(isRetrying)
            }
            .padding(.top, 80)
        }
    }

    private func retry() async {
        isRetrying = true
        defer { isRetrying = false }
        await appState.bootstrap()
    }
}
