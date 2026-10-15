/**
 * Every query key the app uses, in one place.
 *
 * A key is what connects a mutation to the queries it invalidates, and those
 * two are always written in different files. Spelling them inline means the
 * connection is a string literal that has to match, with nothing to catch it
 * when one side is renamed - so they live here, as functions, and the compiler
 * checks the arguments.
 *
 * The shape is hierarchical on purpose: `queryKeys.organizations.all` is a
 * prefix of `queryKeys.organizations.detail(id)`, so invalidating the former
 * invalidates every organization query, including ones added later.
 */
export const queryKeys = {
	organizations: {
		all: ["organizations"] as const,
		mine: () => [...queryKeys.organizations.all, "mine"] as const,
		detail: (organizationId: string) =>
			[...queryKeys.organizations.all, "detail", organizationId] as const,
		members: (organizationId: string) =>
			[...queryKeys.organizations.all, "members", organizationId] as const,
		invitations: (organizationId: string) =>
			[...queryKeys.organizations.all, "invitations", organizationId] as const,
	},

	notifications: {
		all: ["notifications"] as const,
		unreadCount: () =>
			[...queryKeys.notifications.all, "unread-count"] as const,
		list: () => [...queryKeys.notifications.all, "list"] as const,
	},

	profile: {
		all: ["profile"] as const,
		me: () => [...queryKeys.profile.all, "me"] as const,
		achievements: () => [...queryKeys.profile.all, "achievements"] as const,
		streaks: () => [...queryKeys.profile.all, "streaks"] as const,
	},

	engagements: {
		all: ["engagements"] as const,
		mineUpcoming: () =>
			[...queryKeys.engagements.all, "mine", "upcoming"] as const,
	},

	dashboard: {
		all: ["dashboard"] as const,
		layout: () => [...queryKeys.dashboard.all, "layout"] as const,
		organization: (organizationId: string) =>
			[...queryKeys.dashboard.all, "organization", organizationId] as const,
	},
} as const;
