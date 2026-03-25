using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Game.States;

namespace Nyvorn;

public class Game1 : Game
{
    private static readonly Color ElyraSkyBaseColor = new Color(143, 211, 255);
    private const int TargetWidth = 1920;
    private const int TargetHeight = 1080;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private StateMachine _stateMachine;
    private KeyboardState _previousKeyboard;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = TargetWidth;
        _graphics.PreferredBackBufferHeight = TargetHeight;
        _graphics.HardwareModeSwitch = false;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _stateMachine = new StateMachine();
        _stateMachine.PushState(new WorldSelectState(GraphicsDevice, Content, _stateMachine));
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
            Exit();

        if (keyboard.IsKeyDown(Keys.F11) && !_previousKeyboard.IsKeyDown(Keys.F11))
            ToggleFullscreen();

        _stateMachine.Update(gameTime);
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(ElyraSkyBaseColor);

        _stateMachine.Draw(gameTime, _spriteBatch);

        base.Draw(gameTime);
    }

    private void ToggleFullscreen()
    {
        _graphics.IsFullScreen = !_graphics.IsFullScreen;
        _graphics.PreferredBackBufferWidth = TargetWidth;
        _graphics.PreferredBackBufferHeight = TargetHeight;
        _graphics.ApplyChanges();
    }
}
