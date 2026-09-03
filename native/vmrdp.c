/*
 * vmrdp — a display-only client for the Hyper-V console service.
 *
 * Connects to vmms on TCP 2179 with the VM id as the preconnection blob (the Basic Session VMConnect opens),
 * signs the current user in through the native SSPI (NLA, single sign-on — FreeRDP is given no identity),
 * lets FreeRDP decode the graphics into a BGRA framebuffer, and reports every repainted rectangle to the host
 * application. Input is opt-in: a session starts with its input gate shut and drops every vmrdp_mouse /
 * vmrdp_key call until vmrdp_set_input(session, 1) opens it.
 */
#include <winsock2.h>
#include <windows.h>
#include <stdio.h>
#include <freerdp/freerdp.h>
#include <freerdp/settings.h>
#include <freerdp/gdi/gdi.h>
#include <freerdp/error.h>
#include <freerdp/input.h>
#include <freerdp/scancode.h>

#define VMRDP_API __declspec(dllexport)

typedef void (*vmrdp_frame_cb)(void* user, const void* pixels, int width, int height, int stride,
                               int x, int y, int w, int h);
typedef void (*vmrdp_status_cb)(void* user, int state, unsigned int code, const char* text);

enum { VMRDP_CONNECTING = 1, VMRDP_CONNECTED = 2, VMRDP_RESIZED = 3, VMRDP_DISCONNECTED = 4, VMRDP_FAILED = 5 };

typedef struct
{
    rdpContext context;      /* must be first: FreeRDP allocates the context with our ContextSize */
    vmrdp_frame_cb on_frame;
    vmrdp_status_cb on_status;
    void* user;
    volatile LONG connected;
} vmrdp_context;

typedef struct
{
    freerdp* instance;
    HANDLE thread;
    volatile LONG stop;
    volatile LONG input_open;
} vmrdp_session;

static void report(vmrdp_context* ctx, int state, UINT32 code, const char* text)
{
    if (ctx->on_status) ctx->on_status(ctx->user, state, code, text ? text : "");
}

/* Called by the gdi after every batch of updates; the invalid region is what changed since the last call. */
static BOOL vmrdp_end_paint(rdpContext* context)
{
    vmrdp_context* ctx = (vmrdp_context*)context;
    rdpGdi* gdi = context->gdi;
    if (!gdi || !gdi->primary || !gdi->primary->hdc || !gdi->primary->hdc->hwnd) return TRUE;

    HGDI_RGN invalid = gdi->primary->hdc->hwnd->invalid;
    if (invalid->null) return TRUE;

    int x = invalid->x, y = invalid->y, w = invalid->w, h = invalid->h;
    if (x < 0) { w += x; x = 0; }
    if (y < 0) { h += y; y = 0; }
    if (x + w > (int)gdi->width) w = gdi->width - x;
    if (y + h > (int)gdi->height) h = gdi->height - y;
    if (w > 0 && h > 0 && ctx->on_frame)
        ctx->on_frame(ctx->user, gdi->primary_buffer, gdi->width, gdi->height, gdi->stride, x, y, w, h);
    return TRUE;
}

/* The VM changed its monitor resolution: let the gdi reallocate, then tell the host. */
static BOOL vmrdp_desktop_resize(rdpContext* context)
{
    vmrdp_context* ctx = (vmrdp_context*)context;
    UINT32 width = freerdp_settings_get_uint32(context->settings, FreeRDP_DesktopWidth);
    UINT32 height = freerdp_settings_get_uint32(context->settings, FreeRDP_DesktopHeight);
    if (!gdi_resize(context->gdi, width, height)) return FALSE;
    report(ctx, VMRDP_RESIZED, (width << 16) | height, "resized");
    return TRUE;
}

static BOOL vmrdp_post_connect(freerdp* instance)
{
    vmrdp_context* ctx = (vmrdp_context*)instance->context;
    if (!gdi_init(instance, PIXEL_FORMAT_BGRA32)) return FALSE;

    rdpUpdate* update = instance->context->update;
    update->EndPaint = vmrdp_end_paint;
    update->DesktopResize = vmrdp_desktop_resize;

    rdpGdi* gdi = instance->context->gdi;
    InterlockedExchange(&ctx->connected, 1);
    report(ctx, VMRDP_CONNECTED, (gdi->width << 16) | gdi->height, "connected");
    return TRUE;
}

static void vmrdp_post_disconnect(freerdp* instance)
{
    InterlockedExchange(&((vmrdp_context*)instance->context)->connected, 0);
    gdi_free(instance);
}

/* The host is this machine (or one we administer); the console certificate is vmms' self-signed one. */
static DWORD vmrdp_verify_certificate(freerdp* instance, const char* host, UINT16 port, const char* common_name,
                                      const char* subject, const char* issuer, const char* fingerprint, DWORD flags)
{
    (void)instance; (void)host; (void)port; (void)common_name; (void)subject; (void)issuer; (void)fingerprint; (void)flags;
    return 2; /* accept for this session only */
}

static DWORD WINAPI vmrdp_thread(LPVOID arg)
{
    vmrdp_session* s = (vmrdp_session*)arg;
    freerdp* instance = s->instance;
    vmrdp_context* ctx = (vmrdp_context*)instance->context;

    report(ctx, VMRDP_CONNECTING, 0, "connecting");
    if (!freerdp_connect(instance))
    {
        UINT32 err = freerdp_get_last_error(instance->context);
        report(ctx, VMRDP_FAILED, err, freerdp_get_last_error_string(err));
        return 1;
    }

    while (!s->stop && !freerdp_shall_disconnect_context(instance->context))
    {
        HANDLE handles[MAXIMUM_WAIT_OBJECTS];
        DWORD count = freerdp_get_event_handles(instance->context, handles, MAXIMUM_WAIT_OBJECTS);
        if (count == 0) break;
        if (WaitForMultipleObjects(count, handles, FALSE, 250) == WAIT_FAILED) break;
        if (!freerdp_check_event_handles(instance->context)) break;
    }

    UINT32 err = freerdp_get_last_error(instance->context);
    freerdp_disconnect(instance);
    report(ctx, VMRDP_DISCONNECTED, err, err ? freerdp_get_last_error_string(err) : "disconnected");
    return 0;
}

/*
 * Open a console. host: Hyper-V host; vm_id: the VM GUID. The current user signs in (single sign-on).
 * Callbacks run on the session thread. Returns NULL when FreeRDP could not be set up.
 */
VMRDP_API vmrdp_session* vmrdp_open(const char* host, int port, const char* vm_id,
                                    vmrdp_frame_cb on_frame, vmrdp_status_cb on_status, void* user_data)
{
    /* FreeRDP's core resolves names with getaddrinfo but leaves Winsock start-up to the client: that is us. */
    static LONG winsock_ready = 0;
    if (InterlockedCompareExchange(&winsock_ready, 1, 0) == 0)
    {
        WSADATA wsa;
        WSAStartup(MAKEWORD(2, 2), &wsa);
    }

    freerdp* instance = freerdp_new();
    if (!instance) return NULL;

    instance->ContextSize = sizeof(vmrdp_context);
    instance->PostConnect = vmrdp_post_connect;
    instance->PostDisconnect = vmrdp_post_disconnect;
    instance->VerifyCertificateEx = vmrdp_verify_certificate;
    /* No AuthenticateEx: FreeRDP then hands a null identity to the native SSPI = the logged-on user. */

    if (!freerdp_context_new(instance))
    {
        freerdp_free(instance);
        return NULL;
    }

    vmrdp_context* ctx = (vmrdp_context*)instance->context;
    ctx->on_frame = on_frame;
    ctx->on_status = on_status;
    ctx->user = user_data;

    rdpSettings* st = instance->context->settings;
    BOOL ok = TRUE;
    ok &= freerdp_settings_set_string(st, FreeRDP_ServerHostname, host);
    ok &= freerdp_settings_set_uint32(st, FreeRDP_ServerPort, (UINT32)(port > 0 ? port : 2179));
    ok &= freerdp_settings_set_bool(st, FreeRDP_VmConnectMode, TRUE);
    ok &= freerdp_settings_set_string(st, FreeRDP_PreconnectionBlob, vm_id);
    ok &= freerdp_settings_set_bool(st, FreeRDP_SendPreconnectionPdu, TRUE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_NegotiateSecurityLayer, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_NlaSecurity, TRUE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_TlsSecurity, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_RdpSecurity, FALSE);
    ok &= freerdp_settings_set_string(st, FreeRDP_AuthenticationServiceClass, "Microsoft Virtual Console Service");
    ok &= freerdp_settings_set_bool(st, FreeRDP_IgnoreCertificate, TRUE);
    ok &= freerdp_settings_set_uint32(st, FreeRDP_ColorDepth, 32);
    ok &= freerdp_settings_set_bool(st, FreeRDP_SoftwareGdi, TRUE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_SupportGraphicsPipeline, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_RemoteFxCodec, TRUE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_NSCodec, TRUE);
    /* vmms never answers the connect-time network auto-detect; with it enabled FreeRDP sits in
     * CONNECT_TIME_AUTO_DETECT_REQUEST until a 5 s timeout before finishing the handshake. */
    ok &= freerdp_settings_set_bool(st, FreeRDP_NetworkAutoDetect, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_SupportMultitransport, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_SupportDynamicChannels, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_DeviceRedirection, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_RedirectClipboard, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_AudioPlayback, FALSE);
    ok &= freerdp_settings_set_bool(st, FreeRDP_AudioCapture, FALSE);

    vmrdp_session* s = ok ? (vmrdp_session*)calloc(1, sizeof(vmrdp_session)) : NULL;
    if (s)
    {
        s->instance = instance;
        s->thread = CreateThread(NULL, 0, vmrdp_thread, s, 0, NULL);
    }
    if (!s || !s->thread)
    {
        freerdp_context_free(instance);
        freerdp_free(instance);
        free(s);
        return NULL;
    }
    return s;
}

/* Stop the session thread, wait for it, free everything. Safe to call from any thread but the callbacks'. */
VMRDP_API void vmrdp_close(vmrdp_session* s)
{
    if (!s) return;
    InterlockedExchange(&s->stop, 1);
    if (s->instance && s->instance->context) freerdp_abort_connect_context(s->instance->context);
    if (s->thread)
    {
        WaitForSingleObject(s->thread, 10000);
        CloseHandle(s->thread);
    }
    if (s->instance)
    {
        freerdp_context_free(s->instance);
        freerdp_free(s->instance);
    }
    free(s);
}

VMRDP_API const char* vmrdp_version(void)
{
    return freerdp_get_version_string();
}

/* ----- input, opt-in ---------------------------------------------------------------------------------------- */

/* Open (1) or shut (0) the input gate. Shut is the initial state of every session. */
VMRDP_API void vmrdp_set_input(vmrdp_session* s, int enabled)
{
    if (s) InterlockedExchange(&s->input_open, enabled ? 1 : 0);
}

static rdpInput* input_of(vmrdp_session* s)
{
    if (!s || !s->input_open || !s->instance || !s->instance->context) return NULL;
    vmrdp_context* ctx = (vmrdp_context*)s->instance->context;
    if (!ctx->connected) return NULL;
    return s->instance->context->input;
}

/* flags: PTR_FLAGS_* (move 0x0800, down 0x8000, button1/2/3 0x1000/0x2000/0x4000, wheel 0x0200); x, y in remote pixels. */
VMRDP_API int vmrdp_mouse(vmrdp_session* s, unsigned int flags, int x, int y)
{
    rdpInput* in = input_of(s);
    if (!in) return 0;
    return freerdp_input_send_mouse_event(in, (UINT16)flags, (UINT16)x, (UINT16)y) ? 1 : 0;
}

/* code: PC/AT set-1 scan code; extended: the E0 prefix. */
VMRDP_API int vmrdp_key(vmrdp_session* s, int down, unsigned int code, int extended)
{
    rdpInput* in = input_of(s);
    if (!in) return 0;
    UINT32 sc = MAKE_RDP_SCANCODE((BYTE)code, extended ? TRUE : FALSE);
    return freerdp_input_send_keyboard_event_ex(in, down ? TRUE : FALSE, FALSE, sc) ? 1 : 0;
}
