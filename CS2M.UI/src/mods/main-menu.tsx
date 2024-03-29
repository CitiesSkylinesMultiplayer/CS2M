import {ModuleRegistryExtend} from "cs2/modding";
import {MenuButton, Panel, Portal} from "cs2/ui";

function showJoinGame() {

}

export const MainMenuCSMExtend : ModuleRegistryExtend = (Component) => {
    return (props) => {
        const { children, ...otherProps } = props || {};

        return (
            <Component {...otherProps}>
                <MenuButton onClick={showJoinGame}>Multiplayer</MenuButton>
                {children}
            </Component>
        );
    };
}

export const JoinGameMenu = () => {
    return (
        <Panel>
            <div>Ein wenig Text</div>
        </Panel>
    );
}
