import SwiftUI

struct CategoryCard: View {
    let category: CategorySummary

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Image(systemName: "books.vertical.fill")
                .font(.title)
                .foregroundStyle(.tint)

            VStack(alignment: .leading, spacing: 4) {
                Text(category.name).font(.headline).lineLimit(1)
                if let latest = category.latestStoryTitle {
                    Text(latest).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                } else {
                    Text("No stories yet").font(.caption).foregroundStyle(.tertiary).italic()
                }
            }

            Spacer(minLength: 0)

            Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .padding()
        .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading)
        .background(.thinMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(.quaternary, lineWidth: 1))
    }
}
