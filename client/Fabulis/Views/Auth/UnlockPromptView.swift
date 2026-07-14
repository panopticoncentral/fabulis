import SwiftUI

struct UnlockPromptView: View {
    @Environment(AppState.self) private var appState
    @State private var password: String = ""
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @State private var serverURL: String?
    @State private var showingResetConfirm = false
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Image(systemName: "lock.fill")
                    .font(.system(size: 56))
                    .foregroundStyle(.tint)
                Text("Locked").font(.title.bold())
                if let serverURL { Text(serverURL).font(.callout).foregroundStyle(.secondary) }
                SecureField("Vault password", text: $password)
                    .textContentType(.password)
                    .submitLabel(.go)
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .onSubmit {
                        guard !password.isEmpty, !isSubmitting, serverURL != nil else { return }
                        Task { await submit() }
                    }
                if let errorMessage {
                    Text(errorMessage).font(.caption).foregroundStyle(.red)
                }
                Button { Task { await submit() } } label: {
                    Group { if isSubmitting { ProgressView() } else { Text("Unlock") } }
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(password.isEmpty || isSubmitting || serverURL == nil)
                Button("Use a different server") {
                    showingResetConfirm = true
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .disabled(isSubmitting)
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
            .task {
                serverURL = try? await KeychainService.shared.loadServerURL()
                focused = true
            }
        }
    }

    private func submit() async {
        guard let url = serverURL else {
            errorMessage = "No server configured. Choose \u{201C}Use a different server\u{201D} to set one."
            return
        }
        errorMessage = nil
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            _ = try await FabulisAPIClient.shared.unlock(serverURL: url, password: password)
            appState.didReauthenticate()
        } catch APIError.unauthorized {
            errorMessage = "Wrong password."
            password = ""
            focused = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
