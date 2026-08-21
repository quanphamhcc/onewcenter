using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Tests for RemoteCommandHandler: authentication, command parsing, 
/// permission enforcement, and token validation.
/// </summary>
public class RemoteCommandHandlerTests
{
    private PresenterEngine CreateEngine(ServiceOrder? order = null)
    {
        var serviceOrderManager = new ServiceOrderManager("/tmp/test-service-orders");
        var settings = new PresenterSettings();

        if (order != null)
        {
            serviceOrderManager.CreateNew(order.Name, order.Date);
            foreach (var item in order.Items)
            {
                serviceOrderManager.AddItem(item);
            }
        }

        return new PresenterEngine(serviceOrderManager, settings);
    }

    private RemoteCommandHandler CreateHandler(PresenterSettings? settings = null, ServiceOrder? order = null)
    {
        settings ??= new PresenterSettings();
        var engine = CreateEngine(order);
        return new RemoteCommandHandler(engine, settings);
    }

    private ServiceOrder CreateTestServiceOrder()
    {
        return new ServiceOrder
        {
            Name = "Test Service",
            Items = new List<ServiceItem>
            {
                new ServiceItem
                {
                    Title = "Song 1",
                    Type = ServiceItemType.Song,
                    Slides = new List<Slide>
                    {
                        new Slide { Content = new SlideContent { Text = "Verse 1 line 1" } },
                        new Slide { Content = new SlideContent { Text = "Verse 1 line 2" } },
                        new Slide { Content = new SlideContent { Text = "Chorus line 1" } }
                    }
                },
                new ServiceItem
                {
                    Title = "Scripture Reading",
                    Type = ServiceItemType.Scripture,
                    Slides = new List<Slide>
                    {
                        new Slide { Content = new SlideContent { Text = "John 3:16" } }
                    }
                },
                new ServiceItem
                {
                    Title = "Song 2",
                    Type = ServiceItemType.Song,
                    Slides = new List<Slide>
                    {
                        new Slide { Content = new SlideContent { Text = "Song 2 verse" } }
                    }
                }
            }
        };
    }

    // ─── Authentication Tests ───────────────────────────────────────

    [Fact]
    public void Authenticate_CorrectPin_ReturnsSuccessAndToken()
    {
        var settings = new PresenterSettings { RemotePin = "1234" };
        var handler = CreateHandler(settings);

        var result = handler.Authenticate("1234");

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Null(result.Error);
        Assert.Equal(32, result.Token!.Length); // 16 bytes = 32 hex chars
    }

    [Fact]
    public void Authenticate_WrongPin_ReturnsFailure()
    {
        var settings = new PresenterSettings { RemotePin = "1234" };
        var handler = CreateHandler(settings);

        var result = handler.Authenticate("5678");

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Equal("Invalid PIN", result.Error);
    }

    [Fact]
    public void Authenticate_EmptyPin_ReturnsFailure()
    {
        var settings = new PresenterSettings { RemotePin = "1234" };
        var handler = CreateHandler(settings);

        var result = handler.Authenticate("");

        Assert.False(result.Success);
        Assert.Equal("PIN is required", result.Error);
    }

    [Fact]
    public void Authenticate_CustomPin_Works()
    {
        var settings = new PresenterSettings { RemotePin = "9999" };
        var handler = CreateHandler(settings);

        var result = handler.Authenticate("9999");

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
    }

    [Fact]
    public void Authenticate_GeneratesUniqueTokens()
    {
        var settings = new PresenterSettings { RemotePin = "1234" };
        var handler = CreateHandler(settings);

        var result1 = handler.Authenticate("1234");
        var result2 = handler.Authenticate("1234");

        Assert.NotEqual(result1.Token, result2.Token);
    }

    // ─── Token Validation Tests ─────────────────────────────────────

    [Fact]
    public void IsTokenValid_ValidToken_ReturnsTrue()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        Assert.True(handler.IsTokenValid(auth.Token));
    }

    [Fact]
    public void IsTokenValid_InvalidToken_ReturnsFalse()
    {
        var handler = CreateHandler();

        Assert.False(handler.IsTokenValid("nonexistenttoken"));
    }

    [Fact]
    public void IsTokenValid_NullToken_ReturnsFalse()
    {
        var handler = CreateHandler();

        Assert.False(handler.IsTokenValid(null));
    }

    [Fact]
    public void IsTokenValid_EmptyToken_ReturnsFalse()
    {
        var handler = CreateHandler();

        Assert.False(handler.IsTokenValid(""));
    }

    [Fact]
    public void RevokeToken_RemovesToken()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        handler.RevokeToken(auth.Token!);

        Assert.False(handler.IsTokenValid(auth.Token));
    }

    [Fact]
    public void ClearAllTokens_RemovesAllTokens()
    {
        var handler = CreateHandler();
        var auth1 = handler.Authenticate("1234");
        var auth2 = handler.Authenticate("1234");

        handler.ClearAllTokens();

        Assert.False(handler.IsTokenValid(auth1.Token));
        Assert.False(handler.IsTokenValid(auth2.Token));
    }

    // ─── Command Handling: Invalid Token ────────────────────────────

    [Fact]
    public void HandleCommand_InvalidToken_ReturnsNotAuthenticated()
    {
        var handler = CreateHandler();

        var result = handler.HandleCommand("{\"action\":\"nextSlide\"}", "badtoken");

        Assert.False(result.Success);
        Assert.Equal("Not authenticated", result.Error);
    }

    // ─── Command Parsing Tests ──────────────────────────────────────

    [Fact]
    public void HandleCommand_NextSlide_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        // Navigate to first item to have slides
        engine.GoToItem(0);

        var result = handler.HandleCommand("{\"action\":\"nextSlide\"}", auth.Token!);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.StateUpdate);
    }

    [Fact]
    public void HandleCommand_PrevSlide_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        engine.GoToItem(0);
        engine.NextSlideAction();

        var result = handler.HandleCommand("{\"action\":\"prevSlide\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_NextItem_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        engine.GoToItem(0);

        var result = handler.HandleCommand("{\"action\":\"nextItem\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_PrevItem_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        engine.GoToItem(1);

        var result = handler.HandleCommand("{\"action\":\"prevItem\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_GoLive_Succeeds()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goLive\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_GoBlack_Succeeds()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goBlack\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_ShowLogo_Succeeds()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"showLogo\"}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_GoToSlide_WithData_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        engine.GoToItem(0);

        var result = handler.HandleCommand("{\"action\":\"goToSlide\",\"data\":2}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_GoToSlide_MissingData_ReturnsError()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goToSlide\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Missing slide index", result.Error);
    }

    [Fact]
    public void HandleCommand_GoToItem_WithData_Succeeds()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);
        var auth = handler.Authenticate("1234");

        engine.GoToItem(0);

        var result = handler.HandleCommand("{\"action\":\"goToItem\",\"data\":1}", auth.Token!);

        Assert.True(result.Success);
    }

    [Fact]
    public void HandleCommand_GoToItem_MissingData_ReturnsError()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goToItem\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Missing item index", result.Error);
    }

    [Fact]
    public void HandleCommand_UnknownAction_ReturnsError()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"unknownCommand\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Contains("Unknown action", result.Error);
    }

    [Fact]
    public void HandleCommand_InvalidJson_ReturnsError()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("not json at all", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Invalid JSON", result.Error);
    }

    [Fact]
    public void HandleCommand_MissingActionField_ReturnsError()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"foo\":\"bar\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Missing action field", result.Error);
    }

    // ─── Permission Enforcement Tests ───────────────────────────────

    [Fact]
    public void HandleCommand_SlideAdvanceDisabled_NextSlide_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowSlideAdvance = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"nextSlide\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Slide advance not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_SlideAdvanceDisabled_PrevSlide_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowSlideAdvance = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"prevSlide\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Slide advance not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_SlideAdvanceDisabled_GoToSlide_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowSlideAdvance = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goToSlide\",\"data\":0}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Slide advance not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_ItemNavigationDisabled_NextItem_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowItemNavigation = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"nextItem\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Item navigation not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_ItemNavigationDisabled_PrevItem_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowItemNavigation = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"prevItem\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Item navigation not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_ItemNavigationDisabled_GoToItem_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowItemNavigation = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goToItem\",\"data\":0}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Item navigation not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_DisplayControlDisabled_GoLive_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowDisplayControl = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goLive\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Display control not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_DisplayControlDisabled_GoBlack_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowDisplayControl = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"goBlack\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Display control not permitted", result.Error);
    }

    [Fact]
    public void HandleCommand_DisplayControlDisabled_ShowLogo_Rejected()
    {
        var settings = new PresenterSettings { RemoteAllowDisplayControl = false };
        var handler = CreateHandler(settings);
        var auth = handler.Authenticate("1234");

        var result = handler.HandleCommand("{\"action\":\"showLogo\"}", auth.Token!);

        Assert.False(result.Success);
        Assert.Equal("Display control not permitted", result.Error);
    }

    // ─── State Data Tests ───────────────────────────────────────────

    [Fact]
    public void GetCurrentState_ReturnsCorrectServiceItems()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);

        engine.GoToItem(0);

        var state = handler.GetCurrentState();

        Assert.Equal(3, state.Items.Count);
        Assert.Equal("Song 1", state.Items[0].Title);
        Assert.Equal("Song", state.Items[0].Type);
        Assert.Equal("Scripture Reading", state.Items[1].Title);
        Assert.Equal("Scripture", state.Items[1].Type);
    }

    [Fact]
    public void GetCurrentState_ReturnsSlideInfo()
    {
        var order = CreateTestServiceOrder();
        var settings = new PresenterSettings();
        var engine = CreateEngine(order);
        var handler = new RemoteCommandHandler(engine, settings);

        engine.GoToItem(0);

        var state = handler.GetCurrentState();

        Assert.Equal(0, state.SlideIndex);
        Assert.Equal(3, state.TotalSlides);
        Assert.Equal("Verse 1 line 1", state.CurrentText);
        Assert.Equal("Verse 1 line 2", state.NextText);
    }

    [Fact]
    public void GetCurrentState_ReturnsDisplayState()
    {
        var settings = new PresenterSettings();
        var engine = CreateEngine();
        var handler = new RemoteCommandHandler(engine, settings);

        Assert.False(handler.GetCurrentState().IsLive);
        Assert.False(handler.GetCurrentState().IsBlacked);

        engine.GoLive();
        Assert.True(handler.GetCurrentState().IsLive);

        engine.GoBlack();
        Assert.True(handler.GetCurrentState().IsBlacked);
    }

    [Fact]
    public void HandleCommand_SupportsTypeFieldAsAlias()
    {
        var handler = CreateHandler();
        var auth = handler.Authenticate("1234");

        // "type" field should also work as alias for "action"
        var result = handler.HandleCommand("{\"type\":\"goLive\"}", auth.Token!);

        Assert.True(result.Success);
    }
}
