import {getModule} from "cs2/modding";
import {useValue} from "cs2/api";
import {LocalizedString} from "cs2/l10n";
import {MultiplayerScreen} from "./server-browser";
import {hideJoinGame, joinMenuVisible} from "./join-parts";

/** The main menu's multiplayer screen: the game's sub screen frame (title, back button) around the browser. */
export const JoinGameMenu = () => {
    const SubScreen = getModule('game-ui/menu/components/shared/sub-screen/sub-screen.tsx', 'SubScreen');
    const InputActionConsumer = getModule('game-ui/common/input-events/input-action-consumer.tsx', 'InputActionConsumer');
    const AutoNavigationScope = getModule('game-ui/common/focus/auto-navigation-scope.tsx', 'AutoNavigationScope');
    const LoadGameScreenCSS = getModule('game-ui/menu/components/load-game-screen/load-game-screen.module.scss', 'classes');

    const visible: boolean = useValue(joinMenuVisible);
    if (!visible) {
        return <></>;
    }

    return (
        <SubScreen title={<LocalizedString id={"CS2M.UI.Multiplayer"}/>} onClose={hideJoinGame}>
            <InputActionConsumer actions={{}}>
                <div className={LoadGameScreenCSS.content}>
                    <AutoNavigationScope>
                        <MultiplayerScreen/>
                    </AutoNavigationScope>
                </div>
            </InputActionConsumer>
        </SubScreen>
    );
}
