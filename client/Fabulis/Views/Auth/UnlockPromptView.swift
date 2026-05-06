import SwiftUI

struct UnlockPromptView: View {
    @Environment(AppState.self) private var appState
    @State private var password: String = ""
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @State private var serverURL: String?
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
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .padding(.horizontal)
                    .onSubmit {
                        guard !password.isEmpty, !isSubmitting else { return }
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
                .disabled(password.isEmpty || isSubmitting)
                .padding(.horizontal)
                Button("Use a different server") {
                    Task { await appState.resetServer() }
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .disabled(isSubmitting)
            }
            .padding(.top, 80)
            .task {
                serverURL = try? await KeychainService.shared.loadServerURL()
                focused = true
            }
        }
    }

    private func submit() async {
        guard let url = serverURL else { return }
        errorMessage = nil
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            _ = try await FabulisAPIClient.shared.unlock(serverURL: url, password: password)
            appState.didReauthenticate()
        } catch APIError.unauthorized {
            errorMessage = "Wrong password."
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
